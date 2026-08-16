using Microsoft.EntityFrameworkCore;
using SmartApp.Telemetry.Core;

namespace SmartApp.Telemetry.Infrastructure;

public sealed class TelemetryDbContext(DbContextOptions<TelemetryDbContext> options) : DbContext(options)
{
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<TelemetryEvent> TelemetryEvents => Set<TelemetryEvent>();
    public DbSet<ErrorGroup> ErrorGroups => Set<ErrorGroup>();
    public DbSet<ErrorOccurrence> ErrorOccurrences => Set<ErrorOccurrence>();
    public DbSet<DailyApplicationStat> DailyApplicationStats => Set<DailyApplicationStat>();
    public DbSet<DailyEventStat> DailyEventStats => Set<DailyEventStat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<Installation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ApplicationId, x.InstallationId }).IsUnique();
            entity.HasIndex(x => new { x.ApplicationId, x.LastSeenAt });
            entity.Property(x => x.CountryCode).HasMaxLength(2);
            entity.Property(x => x.Architecture).HasMaxLength(20);
            entity.Property(x => x.Language).HasMaxLength(20);
        });

        modelBuilder.Entity<TelemetryEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ApplicationId, x.OccurredAt });
            entity.HasIndex(x => new { x.ApplicationId, x.EventName });
            entity.HasIndex(x => new { x.ApplicationId, x.InstallationId });
            entity.Property(x => x.EventName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PropertiesJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ErrorGroup>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ApplicationId, x.Fingerprint }).IsUnique();
            entity.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ExceptionType).HasMaxLength(300).IsRequired();
        });

        modelBuilder.Entity<ErrorOccurrence>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ErrorGroupId, x.OccurredAt });
            entity.HasIndex(x => new { x.ApplicationId, x.InstallationId });
            entity.Property(x => x.ContextJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<DailyApplicationStat>(entity =>
        {
            entity.HasKey(x => new { x.ApplicationId, x.Date });
        });

        modelBuilder.Entity<DailyEventStat>(entity =>
        {
            entity.HasKey(x => new { x.ApplicationId, x.Date, x.EventName });
            entity.Property(x => x.EventName).HasMaxLength(100);
        });
    }
}
