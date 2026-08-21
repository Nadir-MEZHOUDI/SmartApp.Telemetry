using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using SmartApp.Telemetry.Client;

namespace SmartApp.Telemetry.Sample.Wpf;

public partial class MainWindow : Window
{
    private readonly ITelemetryClient telemetry;
    private static readonly HttpClient DiagnosticsHttp = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private readonly StringBuilder logBuffer = new();
    private const int MaxLogChars = 200_000;

    public MainWindow()
    {
        telemetry = ((App)Application.Current).Telemetry;
        InitializeComponent();

        var app = (App)Application.Current;
        EndpointText.Text = app.Endpoint;
        AppIdText.Text = app.ApplicationName;
        InstallationIdText.Text = app.InstallationId.ToString();
        ConnectionInfo.Text = $"Endpoint: {app.Endpoint}\nApplication: {app.ApplicationName}\nInstallationId: {app.InstallationId}";
        InstallationIdText.ToolTip = app.InstallationId.ToString();
        AppIdText.ToolTip = app.ApplicationName;

        // Bridge telemetry client internal logs -> multiline textbox
        WpfLogBridge.Sink = msg => AppendLogRaw(msg);

        // Global exception hooks -> also log multiline
        Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            LogError("AppDomain.UnhandledException", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogError("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        LogLine($"Telemetry session started. AppId={app.ApplicationName}  InstallationId={app.InstallationId}");
        LogLine($"Endpoint: {app.Endpoint}  Version: 1.0.0");
        LogLine("Events flush automatically every 20s; click 'Flush queue now' to send immediately.");
        LogLine("Enabled log hooks: WpfDispatcher, AppDomain, TaskScheduler, TelemetryClient responses, HTTP probes.");
    }

    private void OnCopyInstallationIdClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(InstallationIdText.Text);
            LogLine($"Copied InstallationId {InstallationIdText.Text} to clipboard.");
        }
        catch (Exception ex)
        {
            LogError("Copy InstallationId failed", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError("DispatcherUnhandledException", e.Exception);
        try { telemetry.TrackException(e.Exception, new { source = "WpfDispatcher" }); } catch (Exception trackEx) { LogError("TrackException failed", trackEx); }
        LogLine($"Dispatcher exception TRACKED: {e.Exception.GetType().Name}: {e.Exception.Message}");
        e.Handled = true;
    }

    private async void OnFlushClicked(object sender, RoutedEventArgs e)
    {
        var sw = Stopwatch.StartNew();
        LogLine($"[FLUSH] starting FlushAsync -> {((App)Application.Current).Endpoint} ...");
        try
        {
            await telemetry.FlushAsync();
            sw.Stop();
            LogLine($"[FLUSH] done in {sw.ElapsedMilliseconds} ms. See [RESPONSE]/[DROP]/[RETRY] lines above for HTTP result. If 202 Accepted, check portal /installations?application={((App)Application.Current).ApplicationName}");
            if (sw.ElapsedMilliseconds < 50) LogLine("[FLUSH] hint: queue was empty or dropped (400 <500). Check [ENQUEUE] and [RESPONSE] lines for validation errors.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogError($"[FLUSH] failed after {sw.ElapsedMilliseconds} ms", ex);
        }
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        var enabled = EnabledCheck.IsChecked == true;
        try { telemetry.SetEnabled(enabled); } catch (Exception ex) { LogError("SetEnabled failed", ex); }
        LogLine($"Telemetry {(enabled ? "enabled" : "disabled")}.");
    }

    private void OnCustomEventClicked(object sender, RoutedEventArgs e)
    {
        var name = EventName.Text.Trim();
        if (name.Length == 0) { LogLine("[TRACK] Event name is required."); return; }
        var properties = ParseProperties(EventProperties.Text);
        try
        {
            telemetry.Track(name, properties);
            LogLine($"[TRACK] event='{name}' properties={properties.Count} ({string.Join(", ", properties.Select(kv => $"{kv.Key}={kv.Value}"))}) -> [ENQUEUE] queued (check next [RESPONSE] after Flush)");
        }
        catch (Exception ex) { LogError($"[TRACK] event='{name}' failed", ex); }
    }

    private void OnFeatureClicked(object sender, RoutedEventArgs e)
    {
        try { telemetry.TrackFeatureUsed("ExportPdf"); LogLine("[TRACK] feature_used: ExportPdf -> queued"); }
        catch (Exception ex) { LogError("TrackFeatureUsed failed", ex); }
    }

    private void OnOperationSucceededClicked(object sender, RoutedEventArgs e)
    {
        try { telemetry.TrackOperationSucceeded("Backup"); LogLine("[TRACK] operation_completed: Backup -> queued"); }
        catch (Exception ex) { LogError("TrackOperationSucceeded failed", ex); }
    }

    private void OnOperationFailedClicked(object sender, RoutedEventArgs e)
    {
        var ex = new TimeoutException("Simulated timeout.");
        try { telemetry.TrackOperationFailed("ExportPdf", ex); LogLine($"[TRACK] operation_failed: ExportPdf + TimeoutException: {ex.Message} -> queued as error (POST /api/v1/telemetry/errors)"); }
        catch (Exception trackEx) { LogError("TrackOperationFailed failed", trackEx); }
    }

    private void OnAppStartedClicked(object sender, RoutedEventArgs e)
    {
        try { telemetry.TrackAppStarted(); LogLine("[TRACK] app_started -> queued"); }
        catch (Exception ex) { LogError("TrackAppStarted failed", ex); }
    }

    private void OnTrackExceptionClicked(object sender, RoutedEventArgs e)
    {
        try { throw new InvalidOperationException("Demo exception from the WPF test harness."); }
        catch (Exception exception)
        {
            try { telemetry.TrackException(exception, new { source = "OnTrackExceptionClicked" }); }
            catch (Exception trackEx) { LogError("TrackException failed", trackEx); }
            LogError("Tracked caught exception (will POST to /errors)", exception);
        }
    }

    private void OnDispatcherExceptionClicked(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => throw new InvalidOperationException("Exception routed through the WPF dispatcher."));
        LogLine("Dispatched an exception; DispatcherUnhandledException handler will track it (watch next [ERROR] + [ENQUEUE] lines).");
    }

    private void OnUnobservedTaskClicked(object sender, RoutedEventArgs e)
    {
        _ = Task.Run(() => throw new InvalidOperationException("Exception in an unobserved task."));
        LogLine("Scheduled unobserved task exception; TaskScheduler hook will log it (check [ERROR] lines). GC collecting...");
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private void OnBurst200Clicked(object sender, RoutedEventArgs e) => Burst(200);
    private void OnBurst1000Clicked(object sender, RoutedEventArgs e) => Burst(1_000);

    private void Burst(int count)
    {
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < count; i++) try { telemetry.Track("burst_event", new { index = i }); } catch (Exception ex) { LogError($"Burst track {i} failed", ex); break; }
        sw.Stop();
        LogLine($"[BURST] Queued {count} burst_event in {sw.ElapsedMilliseconds} ms. Click Flush to send (watch [RESPONSE] 202 vs 400 vs 429).");
    }

    private async void OnCheckApiStatusClicked(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        var endpoint = app.Endpoint.TrimEnd('/');
        var application = app.ApplicationName;

        DiagnosticsOutput.Text = $"Checking {endpoint} ...\n";
        LogLine($"[DIAGNOSTICS] checking {endpoint}  AppId={application}  InstallationId={app.InstallationId}");

        var sb = new StringBuilder();
        sb.AppendLine($"Endpoint: {endpoint}");
        sb.AppendLine($"Application: {application}");
        sb.AppendLine($"InstallationId: {app.InstallationId}");
        sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('-', 48));

        await ProbeEndpointAsync(sb, endpoint, "/health", "Health");
        await ProbeEndpointAsync(sb, endpoint, "/api", "API root");
        var appsOk = await ProbeEndpointAsync(sb, endpoint, "/api/v1/applications", "Applications");
        if (appsOk is not null)
        {
            var contains = appsOk.Contains(application, StringComparison.OrdinalIgnoreCase);
            sb.AppendLine(contains
                ? $"[OK] Application '{application}' FOUND in /api/v1/applications."
                : $"[WARN] Application '{application}' NOT FOUND. Create it: POST {endpoint}/api/v1/applications {{\"name\":\"{application}\",\"slug\":\"{application}\"}}  — this is why events are silently dropped (400 Unknown app).");
            sb.AppendLine();
            LogLine(contains ? $"[DIAGNOSTICS] app FOUND: {application}" : $"[DIAGNOSTICS] app NOT FOUND: {application} -> will 400");
        }

        await ProbeIngestionAsync(sb, endpoint, application);

        sb.AppendLine("--- Hint ---");
        sb.AppendLine("docker compose => http://localhost:8091");
        sb.AppendLine("dotnet run => http://localhost:5000 + https://localhost:5001");
        sb.AppendLine("If 8091 shows FAILED, run: docker compose up --build or set $env:TELEMETRY_ENDPOINT='http://localhost:5000'");

        DiagnosticsOutput.Text = sb.ToString();
        foreach (var line in sb.ToString().Split('\n')) LogLine(line.TrimEnd());
        LogLine("[DIAGNOSTICS] done — see panel + above [RESPONSE] lines.");
    }

    private async void OnProbePortsClicked(object sender, RoutedEventArgs e)
    {
        var candidates = new[] { "http://localhost:8091", "http://localhost:5000", "https://localhost:5001", "http://localhost:8080" };
        DiagnosticsOutput.Text = "Probing candidate ports...\n";
        var sb = new StringBuilder();
        sb.AppendLine($"Probing {candidates.Length} endpoints at {DateTime.Now:HH:mm:ss}");
        sb.AppendLine(new string('-', 48));
        foreach (var ep in candidates) await ProbeEndpointAsync(sb, ep, "/health", $"Health @ {ep}");
        DiagnosticsOutput.Text = sb.ToString();
        foreach (var line in sb.ToString().Split('\n')) LogLine(line.TrimEnd());
        var ok = sb.ToString().Contains(" 200 ", StringComparison.Ordinal);
        LogLine(ok ? "[PROBE] at least one endpoint reachable." : "[PROBE] no endpoint reachable — is Web server running?");
    }

    private async Task<string?> ProbeEndpointAsync(StringBuilder sb, string endpoint, string path, string label)
    {
        var url = endpoint.TrimEnd('/') + path;
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await DiagnosticsHttp.GetAsync(url);
            sw.Stop();
            var body = await response.Content.ReadAsStringAsync();
            var truncated = body.Length > 900 ? body[..900] + " …(truncated)" : body;
            var status = $"{(int)response.StatusCode} {response.StatusCode}";
            sb.AppendLine($"{label}: GET {path}");
            sb.AppendLine($"  -> {status} in {sw.ElapsedMilliseconds} ms");
            sb.AppendLine($"  -> Body: {truncated}");
            sb.AppendLine();
            var logLine = $"[RESPONSE] GET {url} -> {status} {sw.ElapsedMilliseconds}ms body={(string.IsNullOrWhiteSpace(truncated) ? "(empty)" : truncated[..Math.Min(300, truncated.Length)])}";
            if (!response.IsSuccessStatusCode) LogError(logLine, null); else LogLine(logLine);
            return body;
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            sb.AppendLine($"{label}: GET {path}");
            sb.AppendLine($"  -> FAILED in {sw.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is not null) sb.AppendLine($"     Inner: {ex.InnerException.Message}");
            sb.AppendLine($"  -> Is port wrong or server not running?");
            sb.AppendLine();
            LogError($"[RESPONSE] GET {url} FAILED HttpRequestException {sw.ElapsedMilliseconds}ms: {ex.Message} {(ex.InnerException is not null ? $"Inner: {ex.InnerException.Message}" : "")}", ex);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            sw.Stop();
            sb.AppendLine($"{label}: GET {path}");
            sb.AppendLine($"  -> TIMEOUT after {sw.ElapsedMilliseconds} ms: {ex.Message}");
            sb.AppendLine();
            LogError($"[RESPONSE] GET {url} TIMEOUT {sw.ElapsedMilliseconds}ms", ex);
            return null;
        }
        catch (Exception ex)
        {
            sw.Stop();
            sb.AppendLine($"{label}: GET {path}");
            sb.AppendLine($"  -> ERROR: {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine();
            LogError($"[RESPONSE] GET {url} ERROR", ex);
            return null;
        }
    }

    private async Task ProbeIngestionAsync(StringBuilder sb, string endpoint, string application)
    {
        var url = endpoint.TrimEnd('/') + "/api/v1/telemetry/events";
        var payload = new { events = new[] { new { application, installationId = Guid.NewGuid(), eventName = "diagnostics_probe", timestamp = DateTimeOffset.UtcNow, context = new { appVersion = "1.0.0", operatingSystem = "Windows", language = "en" }, properties = new { probe = true } } } };
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await DiagnosticsHttp.PostAsJsonAsync(url, payload);
            sw.Stop();
            var body = await response.Content.ReadAsStringAsync();
            var truncated = body.Length > 900 ? body[..900] + " …(truncated)" : body;
            sb.AppendLine($"Ingestion probe: POST /api/v1/telemetry/events");
            sb.AppendLine($"  -> {(int)response.StatusCode} {response.StatusCode} in {sw.ElapsedMilliseconds} ms");
            sb.AppendLine($"  -> Body: {(string.IsNullOrWhiteSpace(truncated) ? "(empty — 202 Accepted is expected)" : truncated)}");
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted) sb.AppendLine($"  -> OK — check dashboard for 'diagnostics_probe'.");
            else if ((int)response.StatusCode == 400) sb.AppendLine($"  -> 400: app unknown/disabled or validation failed.");
            else if ((int)response.StatusCode == 429) sb.AppendLine($"  -> 429 Rate limited.");
            sb.AppendLine();
            var level = response.IsSuccessStatusCode ? (Action<string>)LogLine : s => LogError(s, null);
            level($"[RESPONSE] POST {url} -> {(int)response.StatusCode} {response.StatusCode} {sw.ElapsedMilliseconds}ms body={(string.IsNullOrWhiteSpace(truncated) ? "(empty)" : truncated[..Math.Min(400, truncated.Length)])}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            sb.AppendLine($"Ingestion probe: POST /api/v1/telemetry/events -> ERROR after {sw.ElapsedMilliseconds} ms: {ex.Message}");
            sb.AppendLine();
            LogError($"[RESPONSE] POST {url} probe ERROR", ex);
        }
    }

    // ---- multiline textbox logger ----

    private void OnClearLogClicked(object sender, RoutedEventArgs e)
    {
        logBuffer.Clear();
        if (LogBox is not null) LogBox.Text = string.Empty;
        WpfLogBridge.Write("[LOG] cleared.");
    }

    private void OnCopyLogClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = LogBox?.Text ?? logBuffer.ToString();
            Clipboard.SetText(text);
            LogLine($"[LOG] copied {text.Length} chars to clipboard.");
        }
        catch (Exception ex) { LogError("Copy log failed", ex); }
    }

    private void LogLine(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) { AppendLogRaw(string.Empty); return; }
        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
        AppendLogRaw($"{ts}  {message}");
    }

    private void LogError(string context, Exception? ex)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
        if (ex is null) AppendLogRaw($"{ts}  [ERROR] {context}");
        else
        {
            var firstLine = $"{ts}  [ERROR] {context}: {ex.GetType().Name}: {ex.Message}";
            var details = ex.ToString();
            // keep first line + full stack as multiline block
            AppendLogRaw($"{firstLine}\n{details}");
        }
    }

    private void AppendLogRaw(string message)
    {
        // always marshal to UI thread, handle re-entrancy from WpfLogBridge (background thread)
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendLogRaw(message));
            return;
        }

        if (LogBox is null)
        {
            logBuffer.AppendLine(message);
            return;
        }

        // append to buffer + textbox, keep size bounded
        logBuffer.AppendLine(message);
        if (logBuffer.Length > MaxLogChars)
        {
            var trimmed = logBuffer.ToString()[^MaxLogChars..];
            logBuffer.Clear();
            logBuffer.Append(trimmed);
            LogBox.Text = trimmed;
        }
        else
        {
            LogBox.AppendText(message + Environment.NewLine);
        }

        if (AutoScrollCheck?.IsChecked == true)
        {
            LogBox.CaretIndex = LogBox.Text.Length;
            LogScroll?.ScrollToEnd();
        }
    }

    private static Dictionary<string, object> ParseProperties(string text)
    {
        var result = new Dictionary<string, object>();
        foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Length > 0) result[parts[0]] = parts[1];
        }
        return result;
    }
}
