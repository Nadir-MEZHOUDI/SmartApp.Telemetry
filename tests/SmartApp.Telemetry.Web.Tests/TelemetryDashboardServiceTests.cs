using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartApp.Telemetry.Core;
using SmartApp.Telemetry.Infrastructure;
using Xunit;

namespace SmartApp.Telemetry.Web.Tests;

public sealed class TelemetryDashboardServiceTests
{
    private static async Task<(TelemetryDbContext Db, TelemetryDashboardService Service)> CreateSeededAsync()
    {
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TelemetryDbContext(options);
        var now = DateTime.UtcNow;

        var appOne = new Application { Id = Guid.NewGuid(), Name = "One", Slug = "one" };
        var appTwo = new Application { Id = Guid.NewGuid(), Name = "Two", Slug = "two", IsEnabled = false };
        db.Applications.AddRange(appOne, appTwo);

        var activeToday = new Installation
        {
            Id = Guid.NewGuid(), ApplicationId = appOne.Id, InstallationId = Guid.CreateVersion7(),
            FirstSeenAt = now.AddDays(-1), LastSeenAt = now, CurrentVersion = "2.0", CountryCode = "DZ",
            OperatingSystem = "Windows"
        };
        var activeLastWeek = new Installation
        {
            Id = Guid.NewGuid(), ApplicationId = appOne.Id, InstallationId = Guid.CreateVersion7(),
            FirstSeenAt = now.AddDays(-10), LastSeenAt = now.AddDays(-3), CurrentVersion = "1.5", CountryCode = "FR"
        };
        var inactive = new Installation
        {
            Id = Guid.NewGuid(), ApplicationId = appOne.Id, InstallationId = Guid.CreateVersion7(),
            FirstSeenAt = now.AddDays(-60), LastSeenAt = now.AddDays(-40), CurrentVersion = "1.0"
        };
        db.Installations.AddRange(activeToday, activeLastWeek, inactive);

        db.TelemetryEvents.AddRange(
            new TelemetryEvent { ApplicationId = appOne.Id, InstallationId = activeToday.InstallationId, EventName = "feature_used", PropertiesJson = JsonSerializer.Serialize(new { feature = "ExportPdf" }), OccurredAt = now },
            new TelemetryEvent { ApplicationId = appOne.Id, InstallationId = activeToday.InstallationId, EventName = "feature_used", PropertiesJson = JsonSerializer.Serialize(new { feature = "ExportPdf" }), OccurredAt = now.AddMinutes(-1) },
            new TelemetryEvent { ApplicationId = appOne.Id, InstallationId = activeLastWeek.InstallationId, EventName = "feature_used", PropertiesJson = JsonSerializer.Serialize(new { feature = "Import" }), OccurredAt = now.AddDays(-5) },
            new TelemetryEvent { ApplicationId = appOne.Id, InstallationId = activeToday.InstallationId, EventName = "app_started", PropertiesJson = "{}", OccurredAt = now });

        var nullGroup = new ErrorGroup { Id = Guid.NewGuid(), ApplicationId = appOne.Id, Fingerprint = "aaa", ExceptionType = "System.NullReferenceException", Title = "NullReferenceException: Save failed", FirstSeenAt = now.AddDays(-2), LastSeenAt = now, FirstSeenVersion = "1.5", LastSeenVersion = "2.0", TotalOccurrences = 2, AffectedInstallations = 1, IsResolved = false, IsRegressed = false };
        var timeoutGroup = new ErrorGroup { Id = Guid.NewGuid(), ApplicationId = appOne.Id, Fingerprint = "bbb", ExceptionType = "System.TimeoutException", Title = "TimeoutException: Network timeout", FirstSeenAt = now.AddDays(-5), LastSeenAt = now.AddDays(-1), FirstSeenVersion = "1.0", LastSeenVersion = "1.5", TotalOccurrences = 1, AffectedInstallations = 1, IsResolved = true, IsRegressed = false, ResolvedAt = now.AddDays(-1) };
        db.ErrorGroups.AddRange(nullGroup, timeoutGroup);

        db.ErrorOccurrences.AddRange(
            new ErrorOccurrence { ErrorGroupId = nullGroup.Id, ApplicationId = appOne.Id, InstallationId = activeToday.InstallationId, ExceptionType = "System.NullReferenceException", Message = "Save failed", ContextJson = "{}", OccurredAt = now },
            new ErrorOccurrence { ErrorGroupId = timeoutGroup.Id, ApplicationId = appOne.Id, InstallationId = activeLastWeek.InstallationId, ExceptionType = "System.TimeoutException", Message = "Network timeout", ContextJson = "{}", OccurredAt = now.AddDays(-1) });

        await db.SaveChangesAsync();
        return (db, new TelemetryDashboardService(db));
    }

    [Fact]
    public async Task Overview_reports_installations_activity_and_crash_free_counts()
    {
        var (db, service) = await CreateSeededAsync();
        await using var _ = db;

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(3, overview.TotalInstallations);
        Assert.Equal(1, overview.ActiveToday);
        Assert.Equal(2, overview.Active7Days);
        Assert.Equal(2, overview.Active30Days);
        Assert.Equal(3, overview.EventsToday);
        Assert.Equal(1, overview.ErrorsToday);
        Assert.Equal(2, overview.CrashFreeInstallations);
        Assert.Equal(2, overview.Applications.Count);
    }

    [Fact]
    public async Task Application_view_reports_versions_countries_and_top_features()
    {
        var (db, service) = await CreateSeededAsync();
        await using var _ = db;

        var application = await service.GetApplicationAsync("one", CancellationToken.None);

        Assert.NotNull(application);
        Assert.Equal(3, application.Summary.Installations);
        Assert.Equal(3, application.Versions.Count);
        Assert.Contains(application.Countries, item => item.Name == "DZ");
        Assert.Equal("ExportPdf", application.Features[0].Name);
        Assert.Equal(2, application.Features[0].Count);
        Assert.Equal(2, application.RecentErrors.Count);
        Assert.NotEmpty(application.Activity);
    }

    [Fact]
    public async Task Application_view_returns_null_for_unknown_slug()
    {
        var (db, service) = await CreateSeededAsync();
        await using var _ = db;

        var application = await service.GetApplicationAsync("missing", CancellationToken.None);

        Assert.Null(application);
    }

    [Fact]
    public async Task Errors_are_filtered_by_status_and_paginated()
    {
        var (db, service) = await CreateSeededAsync();
        await using var _ = db;

        var resolved = await service.GetErrorsAsync(null, "resolved", null, null, null, null, 1, 25, CancellationToken.None);
        var open = await service.GetErrorsAsync(null, "open", null, null, null, null, 1, 25, CancellationToken.None);
        var paged = await service.GetErrorsAsync(null, null, null, null, null, null, 2, 1, CancellationToken.None);

        Assert.Single(resolved.Items);
        Assert.Equal("Resolved", resolved.Items[0].Status);
        Assert.Single(open.Items);
        Assert.Equal("Open", open.Items[0].Status);
        Assert.Equal(2, paged.Total);
        Assert.Single(paged.Items);
    }

    [Fact]
    public async Task Errors_are_filtered_by_search_and_version()
    {
        var (db, service) = await CreateSeededAsync();
        await using var _ = db;

        var bySearch = await service.GetErrorsAsync(null, null, "timeout", null, null, null, 1, 25, CancellationToken.None);
        var byVersion = await service.GetErrorsAsync(null, null, null, "2.0", null, null, 1, 25, CancellationToken.None);
        var nothing = await service.GetErrorsAsync(null, null, "does-not-exist", null, null, null, 1, 25, CancellationToken.None);

        Assert.Single(bySearch.Items);
        Assert.Equal("TimeoutException: Network timeout", bySearch.Items[0].Title);
        Assert.Single(byVersion.Items);
        Assert.Equal("2.0", byVersion.Items[0].LastSeenVersion);
        Assert.Empty(nothing.Items);
    }

    [Fact]
    public async Task Installations_are_filtered_by_country_and_version()
    {
        var (db, service) = await CreateSeededAsync();
        await using var _ = db;

        var algerian = await service.GetInstallationsAsync("one", null, "DZ", null, null, null, 0, 1, 25, CancellationToken.None);
        var version15 = await service.GetInstallationsAsync("one", "1.5", null, null, null, null, 0, 1, 25, CancellationToken.None);

        Assert.Single(algerian.Items);
        Assert.Equal("DZ", algerian.Items[0].CountryCode);
        Assert.Single(version15.Items);
        Assert.Equal("1.5", version15.Items[0].CurrentVersion);
    }

    [Fact]
    public async Task Error_details_include_recent_occurrences()
    {
        var (db, service) = await CreateSeededAsync();
        await using var _ = db;

        var group = db.ErrorGroups.Single(x => x.Fingerprint == "aaa");
        var details = await service.GetErrorAsync("one", group.Id, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(2, details.Occurrences);
        Assert.Single(details.RecentOccurrences);
        Assert.False(details.IsResolved);

        var missing = await service.GetErrorAsync("two", group.Id, CancellationToken.None);
        Assert.Null(missing);
    }
}
