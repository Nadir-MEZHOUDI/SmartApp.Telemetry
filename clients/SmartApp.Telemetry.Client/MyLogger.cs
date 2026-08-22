namespace SmartApp.Telemetry.Client;

/// <summary>
/// Lightweight bridge so WPF sample can show telemetry client internals
/// (HTTP status, bodies, queue failures) in its multiline TextBox.
/// No-op in other hosts.
/// </summary>
public static class MyLogger
{
    public static Action<string>? Sink { get; set; }

    public static void Write(string message)
    {
        try { Sink?.Invoke(message); } catch { /* never throw from logger */ }
    }
}
