using Microsoft.Extensions.Logging;
using SmartApp.Telemetry.Client;

namespace SmartApp.Telemetry.Sample.Wpf;

internal sealed class WpfBridgeLogger : ILogger<TelemetryClient>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var msg = formatter(state, exception);
        var line = exception is null ? $"[{logLevel}] {msg}" : $"[{logLevel}] {msg} | {exception.GetType().Name}: {exception.Message}";
        WpfLogBridge.Write(line);
    }
}
