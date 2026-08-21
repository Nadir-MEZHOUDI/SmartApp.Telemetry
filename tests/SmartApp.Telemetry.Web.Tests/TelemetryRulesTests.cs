using Microsoft.EntityFrameworkCore;
using SmartApp.Telemetry.Core;
using SmartApp.Telemetry.Infrastructure;
using Xunit;

namespace SmartApp.Telemetry.Web.Tests;

public sealed class TelemetryRulesTests
{
    private sealed class TestFactory(DbContextOptions<TelemetryDbContext> options) : IDbContextFactory<TelemetryDbContext>
    {
        public TelemetryDbContext CreateDbContext() => new(options);
        public Task<TelemetryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    [Fact]
    public void Fingerprint_ignores_line_numbers()
    {
        const string first = "at App.Sales.Save() in C:\\src\\Sales.cs:line 10";
        const string second = "at App.Sales.Save() in C:\\src\\Sales.cs:line 99";

        Assert.Equal(TelemetryRules.Fingerprint("System.NullReferenceException", first), TelemetryRules.Fingerprint("System.NullReferenceException", second));
    }

    [Fact]
    public void Sanitise_redacts_secrets()
    {
        var result = TelemetryRules.Sanitise("Host=localhost;Password=secret; token=abc123");

        Assert.DoesNotContain("secret", result, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", result, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ingestion_keeps_applications_isolated()
    {
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var factory = new TestFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Applications.AddRange(
                new Application { Id = Guid.NewGuid(), Name = "One", Slug = "one" },
                new Application { Id = Guid.NewGuid(), Name = "Two", Slug = "two" });
            await db.SaveChangesAsync();
        }

        var service = new TelemetryIngestionService(factory);
        var installationId = Guid.CreateVersion7();
        var result = await service.IngestEventsAsync(
        [
            new TelemetryEventRequest("one", installationId, "app_started", DateTimeOffset.UtcNow, null, null)
        ], "DZ", CancellationToken.None);

        Assert.True(result.Accepted);
        await using var assertDb = await factory.CreateDbContextAsync();
        Assert.Single(assertDb.TelemetryEvents.Where(x => x.ApplicationId == assertDb.Applications.Single(x => x.Slug == "one").Id));
        Assert.Empty(assertDb.TelemetryEvents.Where(x => x.ApplicationId == assertDb.Applications.Single(x => x.Slug == "two").Id));
    }

    [Fact]
    public async Task Errors_with_same_fingerprint_share_a_group()
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

        var service = new TelemetryIngestionService(factory);
        var firstInstallation = Guid.CreateVersion7();
        var secondInstallation = Guid.CreateVersion7();
        var first = new ExceptionTelemetryRequest(
            "one", firstInstallation, "System.InvalidOperationException", "Password=secret", "at One.Save() in C:\\src\\One.cs:line 10", DateTimeOffset.UtcNow, null, null);
        var second = first with { InstallationId = secondInstallation, Message = "Password=other", StackTrace = "at One.Save() in C:\\src\\One.cs:line 99" };

        var result = await service.IngestErrorsAsync([first, second], "DZ", CancellationToken.None);

        Assert.True(result.Accepted);
        await using var assertDb = await factory.CreateDbContextAsync();
        Assert.Single(assertDb.ErrorGroups);
        Assert.Equal(2, assertDb.ErrorGroups.Single().TotalOccurrences);
        Assert.Equal(2, assertDb.ErrorGroups.Single().AffectedInstallations);
        Assert.All(assertDb.ErrorOccurrences, occurrence => Assert.DoesNotContain("Password=", occurrence.Message, StringComparison.Ordinal));
    }
}
