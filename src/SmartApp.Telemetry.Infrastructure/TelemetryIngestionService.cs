using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartApp.Telemetry.Core;

namespace SmartApp.Telemetry.Infrastructure;

public sealed class TelemetryIngestionService(TelemetryDbContext db)
{
    public async Task<(bool Accepted, string? Error)> IngestEventsAsync(
        IReadOnlyList<TelemetryEventRequest> requests,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0 || requests.Count > 50)
            return (false, "A batch must contain between 1 and 50 events.");

        foreach (var request in requests)
        {
            var validationError = TelemetryRules.ValidateEvent(request);
            if (validationError is not null) return (false, validationError);
        }

        var applications = await ResolveApplicationsAsync(requests.Select(x => x.Application), cancellationToken);
        if (applications.Count != requests.Select(x => x.Application).Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return (false, "One or more applications are unknown or disabled.");

        foreach (var request in requests)
        {
            var application = applications[NormalizeSlug(request.Application)];
            await UpsertInstallationAsync(application.Id, request.InstallationId, request.Context, request.Timestamp.UtcDateTime, countryCode, cancellationToken);
            db.TelemetryEvents.Add(new TelemetryEvent
            {
                ApplicationId = application.Id,
                InstallationId = request.InstallationId,
                EventName = request.EventName,
                AppVersion = Limit(request.Context?.AppVersion, 50),
                PropertiesJson = JsonObject(request.Properties),
                OccurredAt = request.Timestamp.UtcDateTime,
                ReceivedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Accepted, string? Error)> IngestErrorsAsync(
        IReadOnlyList<ExceptionTelemetryRequest> requests,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0 || requests.Count > 50)
            return (false, "A batch must contain between 1 and 50 errors.");

        foreach (var request in requests)
        {
            var validationError = TelemetryRules.ValidateException(request);
            if (validationError is not null) return (false, validationError);
        }

        var applications = await ResolveApplicationsAsync(requests.Select(x => x.Application), cancellationToken);
        if (applications.Count != requests.Select(x => x.Application).Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return (false, "One or more applications are unknown or disabled.");

        var groups = new Dictionary<(Guid AppId, string Fingerprint), ErrorGroup>();
        var countedInstallations = new HashSet<(Guid GroupId, Guid InstallationId)>();

        foreach (var request in requests)
        {
            var application = applications[NormalizeSlug(request.Application)];
            var occurredAt = request.Timestamp.UtcDateTime;
            await UpsertInstallationAsync(application.Id, request.InstallationId, request.Context, occurredAt, countryCode, cancellationToken);

            var message = TelemetryRules.Sanitise(request.Message, 10_000);
            var stackTrace = TelemetryRules.Sanitise(request.StackTrace, 30_000);
            var fingerprint = TelemetryRules.Fingerprint(request.ExceptionType, stackTrace);
            var key = (application.Id, fingerprint);

            if (!groups.TryGetValue(key, out var group))
            {
                group = await db.ErrorGroups.SingleOrDefaultAsync(
                    x => x.ApplicationId == application.Id && x.Fingerprint == fingerprint,
                    cancellationToken) ?? new ErrorGroup
                    {
                        Id = Guid.NewGuid(),
                        ApplicationId = application.Id,
                        Fingerprint = fingerprint,
                        ExceptionType = Limit(request.ExceptionType, 300) ?? "Exception",
                        Title = BuildTitle(request.ExceptionType, message),
                        FirstSeenAt = occurredAt,
                        FirstSeenVersion = Limit(request.Context?.AppVersion, 50)
                    };
                groups[key] = group;
                if (db.Entry(group).State == EntityState.Detached) db.ErrorGroups.Add(group);
            }

            var wasResolved = group.IsResolved;
            group.LastSeenAt = occurredAt > group.LastSeenAt ? occurredAt : group.LastSeenAt;
            group.LastSeenVersion = Limit(request.Context?.AppVersion, 50);
            group.TotalOccurrences++;
            group.IsResolved = false;
            group.IsRegressed = wasResolved || group.IsRegressed;

            var installationWasSeen = await db.ErrorOccurrences.AnyAsync(
                x => x.ErrorGroupId == group.Id && x.InstallationId == request.InstallationId,
                cancellationToken);
            if (!installationWasSeen && countedInstallations.Add((group.Id, request.InstallationId)))
                group.AffectedInstallations++;

            db.ErrorOccurrences.Add(new ErrorOccurrence
            {
                ErrorGroupId = group.Id,
                ApplicationId = application.Id,
                InstallationId = request.InstallationId,
                AppVersion = Limit(request.Context?.AppVersion, 50),
                ExceptionType = Limit(request.ExceptionType, 300) ?? "Exception",
                Message = message,
                StackTrace = stackTrace,
                ContextJson = JsonSerializer.Serialize(new { request.Context, request.AdditionalContext }),
                OccurredAt = occurredAt
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Accepted, string? Error)> HeartbeatAsync(
        HeartbeatRequest request,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        if (request.InstallationId == Guid.Empty || string.IsNullOrWhiteSpace(request.Application))
            return (false, "Application and InstallationId are required.");
        var application = await db.Applications.SingleOrDefaultAsync(
            x => x.Slug == NormalizeSlug(request.Application) && x.IsEnabled,
            cancellationToken);
        if (application is null) return (false, "Application is unknown or disabled.");
        await UpsertInstallationAsync(application.Id, request.InstallationId, request.Context, request.Timestamp.UtcDateTime, countryCode, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task MarkErrorResolvedAsync(Guid errorId, string? version, CancellationToken cancellationToken)
    {
        var group = await db.ErrorGroups.SingleOrDefaultAsync(x => x.Id == errorId, cancellationToken)
            ?? throw new KeyNotFoundException("Error group was not found.");
        group.IsResolved = true;
        group.IsRegressed = false;
        group.ResolvedAt = DateTime.UtcNow;
        group.ResolvedInVersion = Limit(version, 50);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<string, Application>> ResolveApplicationsAsync(IEnumerable<string> slugs, CancellationToken cancellationToken)
    {
        var normalized = slugs.Select(NormalizeSlug).Distinct(StringComparer.Ordinal).ToArray();
        var rows = await db.Applications
            .Where(x => normalized.Contains(x.Slug) && x.IsEnabled)
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(x => x.Slug, StringComparer.OrdinalIgnoreCase);
    }

    private async Task UpsertInstallationAsync(
        Guid applicationId,
        Guid installationId,
        TelemetryContext? context,
        DateTime seenAt,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        var installation = db.ChangeTracker.Entries<Installation>()
            .Select(x => x.Entity)
            .SingleOrDefault(x => x.ApplicationId == applicationId && x.InstallationId == installationId)
            ?? await db.Installations.SingleOrDefaultAsync(
                x => x.ApplicationId == applicationId && x.InstallationId == installationId,
                cancellationToken);
        if (installation is null)
        {
            db.Installations.Add(new Installation
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                InstallationId = installationId,
                FirstSeenAt = seenAt,
                LastSeenAt = seenAt,
                FirstVersion = Limit(context?.AppVersion, 50),
                CurrentVersion = Limit(context?.AppVersion, 50),
                CountryCode = Limit(countryCode, 2)?.ToUpperInvariant(),
                OperatingSystem = Limit(context?.OperatingSystem, 100),
                OperatingSystemVersion = Limit(context?.OperatingSystemVersion, 100),
                Architecture = Limit(context?.Architecture, 20),
                Language = Limit(context?.Language, 20),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return;
        }

        installation.LastSeenAt = seenAt > installation.LastSeenAt ? seenAt : installation.LastSeenAt;
        installation.CurrentVersion = Limit(context?.AppVersion, 50) ?? installation.CurrentVersion;
        installation.CountryCode = Limit(countryCode, 2)?.ToUpperInvariant() ?? installation.CountryCode;
        installation.OperatingSystem = Limit(context?.OperatingSystem, 100) ?? installation.OperatingSystem;
        installation.OperatingSystemVersion = Limit(context?.OperatingSystemVersion, 100) ?? installation.OperatingSystemVersion;
        installation.Architecture = Limit(context?.Architecture, 20) ?? installation.Architecture;
        installation.Language = Limit(context?.Language, 20) ?? installation.Language;
        installation.UpdatedAt = DateTime.UtcNow;
    }

    private static string NormalizeSlug(string value) => value.Trim().ToLowerInvariant();
    private static string? Limit(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
    private static string JsonObject(JsonElement? value) => value is { } element && element.ValueKind == JsonValueKind.Object ? element.GetRawText() : "{}";
    private static string BuildTitle(string exceptionType, string message) => string.IsNullOrWhiteSpace(message) ? exceptionType : $"{exceptionType}: {message[..Math.Min(160, message.Length)]}";
}
