using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartApp.Telemetry.Core;
using SmartApp.Telemetry.Infrastructure;
using Xunit;

namespace SmartApp.Telemetry.Web.Tests;

public sealed class TelemetryIngestionTests
{
    private static async Task<(TelemetryDbContext Db, TelemetryIngestionService Service)> CreateServiceAsync()
    {
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TelemetryDbContext(options);
        db.Applications.Add(new Application { Id = Guid.NewGuid(), Name = "One", Slug = "one" });
        await db.SaveChangesAsync();
        return (db, new TelemetryIngestionService(db));
    }

    private static TelemetryEventRequest Event(string? eventName = "app_started", JsonElement? properties = null) =>
        new("one", Guid.CreateVersion7(), eventName ?? "app_started", DateTimeOffset.UtcNow, null, properties);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Events_reject_unknown_event_names()
    {
        var (db, service) = await CreateServiceAsync();
        await using var _ = db;

        var result = await service.IngestEventsAsync([Event("custom_event")], null, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("not allowed", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Events_reject_unknown_applications()
    {
        var (db, service) = await CreateServiceAsync();
        await using var _ = db;

        var result = await service.IngestEventsAsync([Event() with { Application = "missing" }], null, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("unknown or disabled", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Events_reject_empty_and_oversized_batches()
    {
        var (db, service) = await CreateServiceAsync();
        await using var _ = db;

        var empty = await service.IngestEventsAsync([], null, CancellationToken.None);
        var oversized = await service.IngestEventsAsync(Enumerable.Repeat(Event(), 51).ToArray(), null, CancellationToken.None);

        Assert.False(empty.Accepted);
        Assert.False(oversized.Accepted);
    }

    [Fact]
    public async Task Events_reject_too_many_properties()
    {
        var (db, service) = await CreateServiceAsync();
        await using var _ = db;

        var properties = string.Join(',', Enumerable.Range(0, 31).Select(index => $"\"k{index}\":1"));
        var result = await service.IngestEventsAsync([Event(properties: Json($"{{{properties}}}"))], null, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("at most 30", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Events_reject_oversized_string_properties()
    {
        var (db, service) = await CreateServiceAsync();
        await using var _ = db;

        var oversized = new string('a', 2_001);
        var result = await service.IngestEventsAsync(
            [Event(properties: Json($$"""{"value":"{{oversized}}"}"""))], null, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("at most 2000", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Events_reject_deeply_nested_properties()
    {
        var (db, service) = await CreateServiceAsync();
        await using var _ = db;

        var nested = string.Concat(Enumerable.Repeat("{\"n\":", 10)) + "1" + new string('}', 10);
        var result = await service.IngestEventsAsync([Event(properties: Json(nested))], null, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("nested too deeply", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Errors_reject_oversized_additional_context()
    {
        var (db, service) = await CreateServiceAsync();
        await using var _ = db;

        var oversized = new string('b', 2_001);
        var request = new ExceptionTelemetryRequest(
            "one", Guid.CreateVersion7(), "System.Exception", "message", null,
            DateTimeOffset.UtcNow, null, Json($$"""{"data":"{{oversized}}"}"""));

        var result = await service.IngestErrorsAsync([request], null, CancellationToken.None);

        Assert.False(result.Accepted);
    }

    [Fact]
    public async Task Heartbeat_rejects_unknown_applications_and_accepts_known_ones()
    {
        var (db, service) = await CreateServiceAsync();
        await using var _ = db;

        var installationId = Guid.CreateVersion7();
        var unknown = await service.HeartbeatAsync(
            new HeartbeatRequest("missing", installationId, DateTimeOffset.UtcNow, null), null, CancellationToken.None);
        var accepted = await service.HeartbeatAsync(
            new HeartbeatRequest("one", installationId, DateTimeOffset.UtcNow, new TelemetryContext("1.0", "Windows", "11", "x64", "fr")),
            "DZ", CancellationToken.None);

        Assert.False(unknown.Accepted);
        Assert.True(accepted.Accepted);
        var installation = Assert.Single(db.Installations);
        Assert.Equal(installationId, installation.InstallationId);
        Assert.Equal("DZ", installation.CountryCode);
        Assert.Equal("1.0", installation.CurrentVersion);
    }

    [Fact]
    public async Task Resolved_errors_become_regressed_when_they_recur()
    {
        var (db, service) = await CreateServiceAsync();
        await using var _ = db;

        var installationId = Guid.CreateVersion7();
        var request = new ExceptionTelemetryRequest(
            "one", installationId, "System.NullReferenceException", "boom",
            "at App.Save() in C:\\src\\App.cs:line 10", DateTimeOffset.UtcNow, null, null);

        var ingested = await service.IngestErrorsAsync([request], null, CancellationToken.None);
        Assert.True(ingested.Accepted);

        var group = Assert.Single(db.ErrorGroups);
        Assert.False(group.IsResolved);
        Assert.False(group.IsRegressed);

        await service.MarkErrorResolvedAsync(group.Id, "2.0", CancellationToken.None);
        Assert.True(group.IsResolved);
        Assert.False(group.IsRegressed);

        await service.IngestErrorsAsync([request with { Timestamp = DateTimeOffset.UtcNow.AddMinutes(1) }], null, CancellationToken.None);

        Assert.False(group.IsResolved);
        Assert.True(group.IsRegressed);
    }

    [Fact]
    public async Task New_error_groups_are_counted_from_the_batch_without_per_occurrence_queries()
    {
        var (db, service) = await CreateServiceAsync();
        await using var _ = db;

        var firstInstallation = Guid.CreateVersion7();
        var secondInstallation = Guid.CreateVersion7();
        var requests = new[]
        {
            new ExceptionTelemetryRequest(
                "one", firstInstallation, "System.InvalidOperationException", "first",
                "at One.Save() in C:\\src\\One.cs:line 10", DateTimeOffset.UtcNow, null, null),
            new ExceptionTelemetryRequest(
                "one", firstInstallation, "System.InvalidOperationException", "second",
                "at One.Save() in C:\\src\\One.cs:line 99", DateTimeOffset.UtcNow.AddSeconds(1), null, null),
            new ExceptionTelemetryRequest(
                "one", secondInstallation, "System.InvalidOperationException", "third",
                "at One.Save() in C:\\src\\One.cs:line 100", DateTimeOffset.UtcNow.AddSeconds(2), null, null)
        };

        var result = await service.IngestErrorsAsync(requests, null, CancellationToken.None);

        Assert.True(result.Accepted);
        var group = Assert.Single(db.ErrorGroups);
        Assert.Equal(3, group.TotalOccurrences);
        Assert.Equal(2, group.AffectedInstallations);
        Assert.Equal(3, db.ErrorOccurrences.Count());
    }
}
