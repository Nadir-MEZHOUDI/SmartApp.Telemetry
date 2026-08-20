using Microsoft.EntityFrameworkCore;
using SmartApp.Telemetry.Core;
using SmartApp.Telemetry.Infrastructure;

namespace SmartApp.Telemetry.Web;

public sealed class TelemetryMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TelemetryMaintenanceService> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> MaintenanceFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(1, "MaintenanceFailed"), "Telemetry maintenance failed.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = Math.Max(1, configuration.GetValue("Telemetry:MaintenanceIntervalHours", 24));
        var initialDelaySeconds = Math.Max(0, configuration.GetValue("Telemetry:MaintenanceInitialDelaySeconds", 30));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(initialDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                MaintenanceFailed(logger, exception);
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        var start = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);

        foreach (var application in await db.Applications.AsNoTracking().ToListAsync(cancellationToken))
        {
            var events = await db.TelemetryEvents
                .Where(x => x.ApplicationId == application.Id && x.OccurredAt >= start && x.OccurredAt < end)
                .Select(x => new { x.EventName, x.InstallationId })
                .ToListAsync(cancellationToken);
            var errors = await db.ErrorOccurrences
                .Where(x => x.ApplicationId == application.Id && x.OccurredAt >= start && x.OccurredAt < end)
                .Select(x => x.InstallationId)
                .ToListAsync(cancellationToken);

            var daily = await db.DailyApplicationStats.SingleOrDefaultAsync(
                x => x.ApplicationId == application.Id && x.Date == targetDate, cancellationToken)
                ?? new DailyApplicationStat { ApplicationId = application.Id, Date = targetDate };
            daily.ActiveInstallations = events.Select(x => x.InstallationId).Distinct().LongCount();
            daily.NewInstallations = await db.Installations.LongCountAsync(
                x => x.ApplicationId == application.Id && x.FirstSeenAt >= start && x.FirstSeenAt < end, cancellationToken);
            daily.TotalEvents = events.Count;
            daily.TotalErrors = errors.Count;
            if (db.Entry(daily).State == EntityState.Detached) db.DailyApplicationStats.Add(daily);

            var existingEventStats = await db.DailyEventStats
                .Where(x => x.ApplicationId == application.Id && x.Date == targetDate)
                .ToListAsync(cancellationToken);
            db.DailyEventStats.RemoveRange(existingEventStats);
            foreach (var group in events.GroupBy(x => x.EventName, StringComparer.Ordinal))
                db.DailyEventStats.Add(new DailyEventStat
                {
                    ApplicationId = application.Id,
                    Date = targetDate,
                    EventName = group.Key,
                    TotalCount = group.LongCount(),
                    UniqueInstallations = group.Select(x => x.InstallationId).Distinct().LongCount()
                });
        }

        var eventDays = configuration.GetValue("Telemetry:RawEventRetentionDays", 90);
        var errorDays = configuration.GetValue("Telemetry:ErrorRetentionDays", 180);
        var oldEvents = db.TelemetryEvents.Where(x => x.OccurredAt < DateTime.UtcNow.AddDays(-eventDays));
        var oldErrors = db.ErrorOccurrences.Where(x => x.OccurredAt < DateTime.UtcNow.AddDays(-errorDays));
        if (db.Database.IsInMemory())
        {
            db.TelemetryEvents.RemoveRange(await oldEvents.ToListAsync(cancellationToken));
            db.ErrorOccurrences.RemoveRange(await oldErrors.ToListAsync(cancellationToken));
        }
        else
        {
            await oldEvents.ExecuteDeleteAsync(cancellationToken);
            await oldErrors.ExecuteDeleteAsync(cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}

