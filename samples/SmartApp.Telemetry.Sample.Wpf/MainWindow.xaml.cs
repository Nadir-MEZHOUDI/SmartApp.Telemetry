using System.Windows;
using System.Windows.Threading;
using SmartApp.Telemetry.Client;

namespace SmartApp.Telemetry.Sample.Wpf;

public partial class MainWindow : Window
{
    private readonly ITelemetryClient telemetry;

    public MainWindow()
    {
        telemetry = ((App)Application.Current).Telemetry;
        InitializeComponent();

        var endpoint = Environment.GetEnvironmentVariable("TELEMETRY_ENDPOINT") ?? "http://localhost:8091";
        var application = Environment.GetEnvironmentVariable("TELEMETRY_APP") ?? "sample-wpf";
        ConnectionInfo.Text = $"Endpoint: {endpoint}\nApplication: {application}";

        Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
        Log("Telemetry session started. Events flush automatically; click 'Flush queue now' to send immediately.");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        telemetry.TrackException(e.Exception, new { source = "WpfDispatcher" });
        Log($"Dispatcher exception tracked: {e.Exception.Message}");
        e.Handled = true;
    }

    private async void OnFlushClicked(object sender, RoutedEventArgs e)
    {
        await telemetry.FlushAsync();
        Log("Queue flushed.");
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        var enabled = EnabledCheck.IsChecked == true;
        telemetry.SetEnabled(enabled);
        Log($"Telemetry {(enabled ? "enabled" : "disabled")}.");
    }

    private void OnCustomEventClicked(object sender, RoutedEventArgs e)
    {
        var name = EventName.Text.Trim();
        if (name.Length == 0)
        {
            Log("Event name is required.");
            return;
        }

        var properties = ParseProperties(EventProperties.Text);
        telemetry.Track(name, properties);
        Log($"Tracked event '{name}' with {properties.Count} properties.");
    }

    private void OnFeatureClicked(object sender, RoutedEventArgs e)
    {
        telemetry.TrackFeatureUsed("ExportPdf");
        Log("Tracked feature_used: ExportPdf");
    }

    private void OnOperationSucceededClicked(object sender, RoutedEventArgs e)
    {
        telemetry.TrackOperationSucceeded("Backup");
        Log("Tracked operation_completed: Backup");
    }

    private void OnOperationFailedClicked(object sender, RoutedEventArgs e)
    {
        telemetry.TrackOperationFailed("ExportPdf", new TimeoutException("Simulated timeout."));
        Log("Tracked operation_failed: ExportPdf (with exception)");
    }

    private void OnAppStartedClicked(object sender, RoutedEventArgs e)
    {
        telemetry.TrackAppStarted();
        Log("Tracked app_started.");
    }

    private void OnTrackExceptionClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            throw new InvalidOperationException("Demo exception from the WPF test harness.");
        }
        catch (Exception exception)
        {
            telemetry.TrackException(exception, new { source = "OnTrackExceptionClicked" });
        }

        Log("Tracked a caught exception.");
    }

    private void OnDispatcherExceptionClicked(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
            throw new InvalidOperationException("Exception routed through the WPF dispatcher."));
        Log("Dispatched an exception; the DispatcherUnhandledException handler will track it.");
    }

    private void OnUnobservedTaskClicked(object sender, RoutedEventArgs e)
    {
        _ = Task.Run(() => throw new InvalidOperationException("Exception in an unobserved task."));
        Log("Scheduled an unobserved task exception; the TaskScheduler hook will track it.");
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private void OnBurst200Clicked(object sender, RoutedEventArgs e) => Burst(200);

    private void OnBurst1000Clicked(object sender, RoutedEventArgs e) => Burst(1_000);

    private void Burst(int count)
    {
        for (var i = 0; i < count; i++)
            telemetry.Track("burst_event", new { index = i });
        Log($"Queued {count} burst events.");
    }

    private void Log(string message)
    {
        if (LogList is null) return;
        LogList.Items.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        LogList.ScrollIntoView(LogList.Items[^1]);
    }

    private static Dictionary<string, object> ParseProperties(string text)
    {
        var result = new Dictionary<string, object>();
        foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Length > 0)
                result[parts[0]] = parts[1];
        }

        return result;
    }
}
