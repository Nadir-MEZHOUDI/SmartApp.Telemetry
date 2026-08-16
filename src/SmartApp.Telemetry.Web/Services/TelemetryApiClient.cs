using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartApp.Telemetry.Core;

namespace SmartApp.Telemetry.Web.Services;

public sealed class TelemetryApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http;

    public TelemetryApiClient(HttpClient http) => this.http = http;

    public Task<IReadOnlyList<ApplicationListItem>> GetApplicationsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ApplicationListItem>>("api/v1/applications", cancellationToken);

    public Task<DashboardOverview> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        GetAsync<DashboardOverview>("api/v1/dashboard/overview", cancellationToken);

    public Task<DashboardApplication?> GetApplicationAsync(string slug, CancellationToken cancellationToken = default) =>
        GetOrDefaultAsync<DashboardApplication>($"api/v1/dashboard/applications/{Uri.EscapeDataString(slug)}", cancellationToken);

    public Task<DashboardErrorPage> GetErrorsAsync(ErrorFilters filters, int page, CancellationToken cancellationToken = default)
    {
        var query = Query(
            ("application", filters.Application),
            ("status", filters.Status),
            ("search", filters.Search),
            ("version", filters.Version),
            ("from", filters.From),
            ("to", filters.To),
            ("page", page.ToString()),
            ("pageSize", "25"));
        return GetAsync<DashboardErrorPage>($"api/v1/dashboard/errors{query}", cancellationToken);
    }

    public Task<DashboardInstallationPage> GetInstallationsAsync(InstallationFilters filters, int page, CancellationToken cancellationToken = default)
    {
        var query = Query(
            ("application", filters.Application),
            ("version", filters.Version),
            ("country", filters.Country),
            ("operatingSystem", filters.OperatingSystem),
            ("architecture", filters.Architecture),
            ("language", filters.Language),
            ("activeWithinDays", filters.ActiveWithinDays > 0 ? filters.ActiveWithinDays.ToString() : string.Empty),
            ("page", page.ToString()),
            ("pageSize", "25"));
        return GetAsync<DashboardInstallationPage>($"api/v1/dashboard/installations{query}", cancellationToken);
    }

    public Task<ErrorDetails?> GetErrorAsync(string slug, Guid errorId, CancellationToken cancellationToken = default) =>
        GetOrDefaultAsync<ErrorDetails>(
            $"api/v1/dashboard/applications/{Uri.EscapeDataString(slug)}/errors/{errorId}",
            cancellationToken);

    public async Task ResolveErrorAsync(Guid errorId, string? version, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            $"api/v1/dashboard/errors/{errorId}/resolve",
            new { version },
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Telemetry API returned an empty response.");
    }

    private async Task<T?> GetOrDefaultAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(detail)
                ? $"Telemetry API returned {(int)response.StatusCode}."
                : detail);
    }

    private static string Query(params (string Key, string Value)[] values)
    {
        var encoded = values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
        var query = string.Join("&", encoded);
        return string.IsNullOrWhiteSpace(query) ? string.Empty : $"?{query}";
    }
}

