using Microsoft.EntityFrameworkCore;
using SmartApp.Telemetry.Core;
using SmartApp.Telemetry.Infrastructure;
using Xunit;

namespace SmartApp.Telemetry.Web.Tests;

public sealed class TelemetryAggregationServiceTests
{
    private static async Task<(TelemetryDbContext Db, TelemetryAggregationService Service)> CreateSeededAsync()
    {
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TelemetryDbContext(options);
        var now = DateTime.UtcNow;
        var yesterday = now.Date.AddDays(-1).ToUniversalTime();
        var application = new Application { Id = Guid.NewGuid(), Name = "One", Slug = "one" };
        db.Applications.Add(application);

        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        db.Installations.AddRange(
            new Installation { Id = Guid.NewGuid(), ApplicationId = application.Id, InstallationId = first, FirstSeenAt = yesterday, LastSeenAt = now },
            new Installation { Id = Guid.NewGuid(), ApplicationId = application.Id, InstallationId = second, FirstSeenAt = now.AddDays(-10), LastSeenAt = now });

        db.TelemetryEvents.AddRange(
            new TelemetryEvent { ApplicationId = application.Id, InstallationId = first, EventName = "feature_used", PropertiesJson = "{}", OccurredAt = yesterday },
            new TelemetryEvent { ApplicationId = application.Id, InstallationId = first, EventName = "feature_used", PropertiesJson = "{}", OccurredAt = yesterday.AddMinutes(5) },
            new TelemetryEvent { ApplicationId = application.Id, InstallationId = second, EventName = "app_started", PropertiesJson = "{}", OccurredAt = yesterday.AddMinutes(10) },
            new TelemetryEvent { ApplicationId = application.Id, InstallationId = second, EventName = "app_started", PropertiesJson = "{}", OccurredAt = now });

        db.ErrorOccurrences.Add(
            new ErrorOccurrence { ErrorGroupId = Guid.NewGuid(), ApplicationId = application.Id, InstallationId = first, ExceptionType = "System.Exception", Message = "boom", ContextJson = "{}", OccurredAt = yesterday });

        await db.SaveChangesAsync();
        return (db, new TelemetryAggregationService(db));
    }

    [Fact]
    public async Task RebuildDailyStats_aggregates_events_and_is_idempotent()
    {
        var (db, service) = await CreateSeededAsync();
        await using var _ = db;
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));

        await service.RebuildDailyStatsAsync(yesterday, CancellationToken.None);
        await service.RebuildDailyStatsAsync(yesterday, CancellationToken.None);

        var daily = Assert.Single(db.DailyApplicationStats);
        Assert.Equal(2, daily.ActiveInstallations);
        Assert.Equal(1, daily.NewInstallations);
        Assert.Equal(3, daily.TotalEvents);
        Assert.Equal(1, daily.TotalErrors);

        var eventStats = db.DailyEventStats.ToList();
        Assert.Equal(2, eventStats.Count);
        var feature = eventStats.Single(x => x.EventName == "feature_used");
        Assert.Equal(2, feature.TotalCount);
        Assert.Equal(1, feature.UniqueInstallations);
        var started = eventStats.Single(x => x.EventName == "app_started");
        Assert.Equal(1, started.TotalCount);
        Assert.Equal(1, started.UniqueInstallations);
    }

    [Fact]
    public async Task DeleteExpired_removes_only_rows_beyond_retention()
    {
        var (db, service) = await CreateSeededAsync();
        await using var _ = db;
        var applicationId = db.Applications.Single().Id;
        var oldEvent = new TelemetryEvent { ApplicationId = applicationId, InstallationId = Guid.CreateVersion7(), EventName = "app_started", PropertiesJson = "{}", OccurredAt = DateTime.UtcNow.AddDays(-120) };
        var oldError = new ErrorOccurrence { ErrorGroupId = Guid.NewGuid(), ApplicationId = applicationId, InstallationId = Guid.CreateVersion7(), ExceptionType = "System.Exception", Message = "old", ContextJson = "{}", OccurredAt = DateTime.UtcNow.AddDays(-200) };
        db.TelemetryEvents.Add(oldEvent);
        db.ErrorOccurrences.Add(oldError);
        await db.SaveChangesAsync();

        await service.DeleteExpiredAsync(90, 180, CancellationToken.None);

        Assert.DoesNotContain(db.TelemetryEvents, x => x.Id == oldEvent.Id);
        Assert.DoesNotContain(db.ErrorOccurrences, x => x.Id == oldError.Id);
        Assert.Equal(4, db.TelemetryEvents.Count());
        Assert.Single(db.ErrorOccurrences);
    }
}
