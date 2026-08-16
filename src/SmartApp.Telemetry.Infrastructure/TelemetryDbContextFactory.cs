using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartApp.Telemetry.Infrastructure;

public sealed class TelemetryDbContextFactory : IDesignTimeDbContextFactory<TelemetryDbContext>
{
    public TelemetryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseNpgsql("Host=localhost;Database=telemetry;Username=postgres;Password=postgres")
            .Options;
        return new TelemetryDbContext(options);
    }
}
