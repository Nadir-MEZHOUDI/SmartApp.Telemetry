using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartApp.Telemetry.Core;
using SmartApp.Telemetry.Infrastructure;
using Xunit;

namespace SmartApp.Telemetry.Web.Tests;

public sealed class TelemetryIngestionTests
{
    private sealed class TestFactory(DbContextOptions<TelemetryDbContext> options) : IDbContextFactory<TelemetryDbContext>
    {
        public TelemetryDbContext CreateDbContext() => new(options);
        public Task<TelemetryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private static async Task<(IDbContextFactory<TelemetryDbContext> Factory, TelemetryIngestionService Service)> CreateServiceAsync()
    {
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var factory = new TestFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Applications.Add(new Application { Id = Guid.NewGuid(), Name = "One", Slug = "one" });
            await db.SaveChangesAsync();
        }
        return (factory, new TelemetryIngestionService(factory));
    }

    private static TelemetryEventRequest Event(string? eventName = "app_started", JsonElement? properties = null) =>
        new("one", Guid.CreateVersion7(), eventName ?? "app_started", DateTimeOffset.UtcNow, null, properties);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Events_reject_unknown_event_names()
    {
        var (_, service) = await CreateServiceAsync();

        // "custom_event" is now allowed via regex ^[a-z][a-z0-9_\.\-]{1,99}$ (fix for WPF sample user_action/burst_event)
        // invalid names still rejected — e.g. uppercase / spaces / special chars
        var result = await service.IngestEventsAsync([Event("INVALID EVENT!")], null, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("not allowed", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Events_accept_custom_event_names()
    {
        var (_, service) = await CreateServiceAsync();
        var result = await service.IngestEventsAsync([Event("custom_event")], null, CancellationToken.None);
        Assert.True(result.Accepted);
    }

    [Fact]
    public async Task Events_reject_unknown_applications()
    {
        var (_, service) = await CreateServiceAsync();

        var result = await service.IngestEventsAsync([Event() with { Application = "missing" }], null, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("unknown or disabled", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Events_reject_empty_and_oversized_batches()
    {
        var (_, service) = await CreateServiceAsync();

        var empty = await service.IngestEventsAsync([], null, CancellationToken.None);
        var oversized = await service.IngestEventsAsync(Enumerable.Repeat(Event(), 51).ToArray(), null, CancellationToken.None);

        Assert.False(empty.Accepted);
        Assert.False(oversized.Accepted);
    }

    [Fact]
    public async Task Events_reject_too_many_properties()
    {
        var (_, service) = await CreateServiceAsync();

        var properties = string.Join(',', Enumerable.Range(0, 31).Select(index => $"\"k{index}\":1"));
        var result = await service.IngestEventsAsync([Event(properties: Json($"{{{properties}}}"))], null, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("at most 30", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Events_reject_oversized_string_properties()
    {
        var (_, service) = await CreateServiceAsync();

        var oversized = new string('a', 2_001);
        var result = await service.IngestEventsAsync(
            [Event(properties: Json($$"""{"value":"{{oversized}}"}"""))], null, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("at most 2000", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Events_reject_deeply_nested_properties()
    {
        var (_, service) = await CreateServiceAsync();

        var nested = string.Concat(Enumerable.Repeat("{\"n\":", 10)) + "1" + new string('}', 10);
        var result = await service.IngestEventsAsync([Event(properties: Json(nested))], null, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("nested too deeply", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Errors_reject_oversized_additional_context()
    {
        var (_, service) = await CreateServiceAsync();

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
        var (factory, service) = await CreateServiceAsync();

        var installationId = Guid.CreateVersion7();
        var unknown = await service.HeartbeatAsync(
            new HeartbeatRequest("missing", installationId, DateTimeOffset.UtcNow, null), null, CancellationToken.None);
        var accepted = await service.HeartbeatAsync(
            new HeartbeatRequest("one", installationId, DateTimeOffset.UtcNow, new TelemetryContext("1.0", "Windows", "11", "x64", "fr")),
            "DZ", CancellationToken.None);

        Assert.False(unknown.Accepted);
        Assert.True(accepted.Accepted);
        await using var db = await factory.CreateDbContextAsync();
        var installation = Assert.Single(db.Installations);
        Assert.Equal(installationId, installation.InstallationId);
        Assert.Equal("DZ", installation.CountryCode);
        Assert.Equal("1.0", installation.CurrentVersion);
    }

    [Fact]
    public async Task Resolved_errors_become_regressed_when_they_recur()
    {
        var (factory, service) = await CreateServiceAsync();

        var installationId = Guid.CreateVersion7();
        var request = new ExceptionTelemetryRequest(
            "one", installationId, "System.NullReferenceException", "boom",
            "at App.Save() in C:\\src\\App.cs:line 10", DateTimeOffset.UtcNow, null, null);

        var ingested = await service.IngestErrorsAsync([request], null, CancellationToken.None);
        Assert.True(ingested.Accepted);

        Guid groupId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var group = Assert.Single(db.ErrorGroups);
            Assert.False(group.IsResolved);
            Assert.False(group.IsRegressed);
            groupId = group.Id;
        }

        await service.MarkErrorResolvedAsync(groupId, "2.0", CancellationToken.None);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var group = Assert.Single(db.ErrorGroups);
            Assert.True(group.IsResolved);
            Assert.False(group.IsRegressed);
        }

        await service.IngestErrorsAsync([request with { Timestamp = DateTimeOffset.UtcNow.AddMinutes(1) }], null, CancellationToken.None);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var group = Assert.Single(db.ErrorGroups);
            Assert.False(group.IsResolved);
            Assert.True(group.IsRegressed);
        }
    }

    [Fact]
    public async Task New_error_groups_are_counted_from_the_batch_without_per_occurrence_queries()
    {
        var (factory, service) = await CreateServiceAsync();

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
        await using var db = await factory.CreateDbContextAsync();
        var group = Assert.Single(db.ErrorGroups);
        Assert.Equal(3, group.TotalOccurrences);
        Assert.Equal(2, group.AffectedInstallations);
        Assert.Equal(3, db.ErrorOccurrences.Count());
    }
}
