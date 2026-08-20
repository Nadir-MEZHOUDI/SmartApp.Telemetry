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
        var aggregation = scope.ServiceProvider.GetRequiredService<TelemetryAggregationService>();
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        var eventDays = configuration.GetValue("Telemetry:RawEventRetentionDays", 90);
        var errorDays = configuration.GetValue("Telemetry:ErrorRetentionDays", 180);

        await aggregation.RebuildDailyStatsAsync(targetDate, cancellationToken);
        await aggregation.DeleteExpiredAsync(eventDays, errorDays, cancellationToken);
    }
}
