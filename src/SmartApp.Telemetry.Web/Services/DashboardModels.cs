namespace SmartApp.Telemetry.Web.Services;

public sealed record ApplicationListItem(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    bool IsEnabled,
    DateTime CreatedAt);

public sealed class ErrorFilters
{
    public string Application { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Search { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public sealed class InstallationFilters
{
    public string Application { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public int ActiveWithinDays { get; set; }
}

public sealed class DashboardState
{
    private readonly TelemetryApiClient api;

    public DashboardState(TelemetryApiClient api) => this.api = api;

    public event Action? Changed;
    public IReadOnlyList<ApplicationListItem> Applications { get; private set; } = [];
    public string Scope { get; set; } = string.Empty;
    public bool ApiAvailable { get; private set; }
    public string StatusMessage { get; private set; } = "Connecting to telemetry API";
    public string PageTitle { get; private set; } = "Operational overview";
    public string PageLede { get; private set; } = "A compact read on adoption, activity, and the health of every SmartApp client.";

    public async Task LoadApplicationsAsync()
    {
        try
        {
            Applications = await api.GetApplicationsAsync();
            ApiAvailable = true;
            StatusMessage = $"Updated {DateTime.Now:t}";
        }
        catch (Exception exception)
        {
            ApiAvailable = false;
            StatusMessage = $"Unable to load telemetry data: {exception.Message}";
        }

        Notify();
    }

    public void SetPage(string title, string lede)
    {
        PageTitle = title;
        PageLede = lede;
        Notify();
    }

    public void MarkApiSuccess()
    {
        ApiAvailable = true;
        StatusMessage = $"Updated {DateTime.Now:t}";
        Notify();
    }

    public void MarkApiFailure(Exception exception)
    {
        ApiAvailable = false;
        StatusMessage = $"Unable to load telemetry data: {exception.Message}";
        Notify();
    }

    public void Notify() => Changed?.Invoke();
}

