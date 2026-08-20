using Microsoft.EntityFrameworkCore;
using SmartApp.Telemetry.Core;

namespace SmartApp.Telemetry.Infrastructure;

public sealed class TelemetryAggregationService(TelemetryDbContext db)
{
    private const int DeleteChunkSize = 5_000;

    public async Task RebuildDailyStatsAsync(DateOnly targetDate, CancellationToken cancellationToken)
    {
        var start = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);
        var applicationIds = await db.Applications.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);

        foreach (var applicationId in applicationIds)
        {
            var eventStats = await db.TelemetryEvents.AsNoTracking()
                .Where(x => x.ApplicationId == applicationId && x.OccurredAt >= start && x.OccurredAt < end)
                .GroupBy(x => x.EventName)
                .Select(group => new
                {
                    EventName = group.Key,
                    TotalCount = group.LongCount(),
                    UniqueInstallations = group.Select(x => x.InstallationId).Distinct().LongCount()
                })
                .ToListAsync(cancellationToken);
            var activeInstallations = await db.TelemetryEvents.AsNoTracking()
                .Where(x => x.ApplicationId == applicationId && x.OccurredAt >= start && x.OccurredAt < end)
                .Select(x => x.InstallationId)
                .Distinct()
                .LongCountAsync(cancellationToken);
            var newInstallations = await db.Installations.LongCountAsync(
                x => x.ApplicationId == applicationId && x.FirstSeenAt >= start && x.FirstSeenAt < end,
                cancellationToken);
            var totalErrors = await db.ErrorOccurrences.LongCountAsync(
                x => x.ApplicationId == applicationId && x.OccurredAt >= start && x.OccurredAt < end,
                cancellationToken);

            var daily = await db.DailyApplicationStats.SingleOrDefaultAsync(
                x => x.ApplicationId == applicationId && x.Date == targetDate, cancellationToken)
                ?? new DailyApplicationStat { ApplicationId = applicationId, Date = targetDate };
            daily.ActiveInstallations = activeInstallations;
            daily.NewInstallations = newInstallations;
            daily.TotalEvents = eventStats.Sum(x => x.TotalCount);
            daily.TotalErrors = totalErrors;
            if (db.Entry(daily).State == EntityState.Detached) db.DailyApplicationStats.Add(daily);

            var existingEventStats = await db.DailyEventStats
                .Where(x => x.ApplicationId == applicationId && x.Date == targetDate)
                .ToListAsync(cancellationToken);
            db.DailyEventStats.RemoveRange(existingEventStats);
            foreach (var stat in eventStats)
                db.DailyEventStats.Add(new DailyEventStat
                {
                    ApplicationId = applicationId,
                    Date = targetDate,
                    EventName = stat.EventName,
                    TotalCount = stat.TotalCount,
                    UniqueInstallations = stat.UniqueInstallations
                });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteExpiredAsync(int rawEventRetentionDays, int errorRetentionDays, CancellationToken cancellationToken)
    {
        var eventCutoff = DateTime.UtcNow.AddDays(-rawEventRetentionDays);
        var errorCutoff = DateTime.UtcNow.AddDays(-errorRetentionDays);
        var eventQuery = db.TelemetryEvents.Where(x => x.OccurredAt < eventCutoff);
        var errorQuery = db.ErrorOccurrences.Where(x => x.OccurredAt < errorCutoff);

        if (db.Database.IsRelational())
        {
            await DeleteInChunksAsync(eventQuery, cancellationToken);
            await DeleteInChunksAsync(errorQuery, cancellationToken);
        }
        else
        {
            db.TelemetryEvents.RemoveRange(await eventQuery.ToListAsync(cancellationToken));
            db.ErrorOccurrences.RemoveRange(await errorQuery.ToListAsync(cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task DeleteInChunksAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
        where T : class
    {
        while (true)
        {
            var deleted = await query.Take(DeleteChunkSize).ExecuteDeleteAsync(cancellationToken);
            if (deleted < DeleteChunkSize) break;
        }
    }
}
