using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SmartApp.Telemetry.Infrastructure.Migrations;

[DbContext(typeof(TelemetryDbContext))]
[Migration("20260816190000_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Applications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Applications", x => x.Id));

        migrationBuilder.CreateTable(
            name: "DailyApplicationStats",
            columns: table => new
            {
                ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                ActiveInstallations = table.Column<long>(type: "bigint", nullable: false),
                NewInstallations = table.Column<long>(type: "bigint", nullable: false),
                TotalEvents = table.Column<long>(type: "bigint", nullable: false),
                TotalErrors = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DailyApplicationStats", x => new { x.ApplicationId, x.Date }));

        migrationBuilder.CreateTable(
            name: "DailyEventStats",
            columns: table => new
            {
                ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                EventName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TotalCount = table.Column<long>(type: "bigint", nullable: false),
                UniqueInstallations = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DailyEventStats", x => new { x.ApplicationId, x.Date, x.EventName }));

        migrationBuilder.CreateTable(
            name: "Installations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                FirstVersion = table.Column<string>(type: "text", nullable: true),
                CurrentVersion = table.Column<string>(type: "text", nullable: true),
                CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                OperatingSystem = table.Column<string>(type: "text", nullable: true),
                OperatingSystemVersion = table.Column<string>(type: "text", nullable: true),
                Architecture = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                Language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Installations", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ErrorGroups",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ExceptionType = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Title = table.Column<string>(type: "text", nullable: false),
                FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                FirstSeenVersion = table.Column<string>(type: "text", nullable: true),
                LastSeenVersion = table.Column<string>(type: "text", nullable: true),
                TotalOccurrences = table.Column<long>(type: "bigint", nullable: false),
                AffectedInstallations = table.Column<long>(type: "bigint", nullable: false),
                IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                IsRegressed = table.Column<bool>(type: "boolean", nullable: false),
                ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ResolvedInVersion = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ErrorGroups", x => x.Id));

        migrationBuilder.CreateTable(
            name: "TelemetryEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                EventName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AppVersion = table.Column<string>(type: "text", nullable: true),
                PropertiesJson = table.Column<string>(type: "jsonb", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_TelemetryEvents", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ErrorOccurrences",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ErrorGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                AppVersion = table.Column<string>(type: "text", nullable: true),
                ExceptionType = table.Column<string>(type: "text", nullable: false),
                Message = table.Column<string>(type: "text", nullable: false),
                StackTrace = table.Column<string>(type: "text", nullable: true),
                ContextJson = table.Column<string>(type: "jsonb", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ErrorOccurrences", x => x.Id));

        migrationBuilder.CreateIndex("IX_Applications_Slug", "Applications", "Slug", unique: true);
        migrationBuilder.CreateIndex("IX_Installations_ApplicationId_InstallationId", "Installations", new[] { "ApplicationId", "InstallationId" }, unique: true);
        migrationBuilder.CreateIndex("IX_Installations_ApplicationId_LastSeenAt", "Installations", new[] { "ApplicationId", "LastSeenAt" });
        migrationBuilder.CreateIndex("IX_ErrorGroups_ApplicationId_Fingerprint", "ErrorGroups", new[] { "ApplicationId", "Fingerprint" }, unique: true);
        migrationBuilder.CreateIndex("IX_ErrorOccurrences_ErrorGroupId_OccurredAt", "ErrorOccurrences", new[] { "ErrorGroupId", "OccurredAt" });
        migrationBuilder.CreateIndex("IX_ErrorOccurrences_ApplicationId_InstallationId", "ErrorOccurrences", new[] { "ApplicationId", "InstallationId" });
        migrationBuilder.CreateIndex("IX_TelemetryEvents_ApplicationId_OccurredAt", "TelemetryEvents", new[] { "ApplicationId", "OccurredAt" });
        migrationBuilder.CreateIndex("IX_TelemetryEvents_ApplicationId_EventName", "TelemetryEvents", new[] { "ApplicationId", "EventName" });
        migrationBuilder.CreateIndex("IX_TelemetryEvents_ApplicationId_InstallationId", "TelemetryEvents", new[] { "ApplicationId", "InstallationId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ErrorOccurrences");
        migrationBuilder.DropTable("TelemetryEvents");
        migrationBuilder.DropTable("ErrorGroups");
        migrationBuilder.DropTable("Installations");
        migrationBuilder.DropTable("DailyEventStats");
        migrationBuilder.DropTable("DailyApplicationStats");
        migrationBuilder.DropTable("Applications");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        // The runtime schema is defined in TelemetryDbContext.OnModelCreating.
    }
}
