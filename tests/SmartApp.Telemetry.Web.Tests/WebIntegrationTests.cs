using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace SmartApp.Telemetry.Web.Tests;

public sealed class WebIntegrationTests : IClassFixture<TelemetryWebFactory>
{
    private readonly TelemetryWebFactory factory;

    public WebIntegrationTests(TelemetryWebFactory factory) => this.factory = factory;

    [Fact]
    public async Task Dashboard_redirects_html_clients_to_login()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dashboard_returns_unauthorized_for_non_html_clients()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_requires_an_antiforgery_token()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = "test-password",
            ["returnUrl"] = "/"
        });

        using var response = await client.PostAsync("/login/submit", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_succeeds_with_the_correct_password()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var token = await ReadAntiforgeryTokenAsync(client);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = "test-password",
            ["returnUrl"] = "/",
            ["__RequestVerificationToken"] = token
        });
        using var response = await client.PostAsync("/login/submit", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Login_rejects_the_wrong_password()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var token = await ReadAntiforgeryTokenAsync(client);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = "wrong-password",
            ["returnUrl"] = "/",
            ["__RequestVerificationToken"] = token
        });
        using var response = await client.PostAsync("/login/submit", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=invalid", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticated_clients_can_open_the_dashboard()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var token = await ReadAntiforgeryTokenAsync(client);
        using var login = await client.PostAsync("/login/submit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = "test-password",
            ["returnUrl"] = "/",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_api_requires_the_admin_key()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var missing = await client.GetAsync("/api/v1/dashboard/overview");
        using var wrong = await GetWithKeyAsync(client, "wrong-key");
        using var correct = await GetWithKeyAsync(client, "test-admin-key");

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
    }

    [Fact]
    public async Task Ingestion_is_rate_limited_per_client()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await client.PostAsync("/api/v1/telemetry/events",
                JsonContent.Create(new { events = Array.Empty<object>() }));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        using var limited = await client.PostAsync("/api/v1/telemetry/events",
            JsonContent.Create(new { events = Array.Empty<object>() }));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Applications_can_be_registered_and_listed()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var created = await client.PostAsync("/api/v1/applications",
            JsonContent.Create(new { name = "SmartPharm", slug = "smartpharm" }));
        using var listed = await client.GetAsync("/api/v1/applications");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var body = await listed.Content.ReadAsStringAsync();
        Assert.Contains("smartpharm", body, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> GetWithKeyAsync(HttpClient client, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/dashboard/overview");
        request.Headers.Add("X-Admin-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<string> ReadAntiforgeryTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/login");
        var match = Regex.Match(html, "__RequestVerificationToken[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Antiforgery token not found on the login page.");
        return match.Groups[1].Value;
    }
}

public sealed class TelemetryWebFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = "true",
                ["Dashboard:Password"] = "test-password",
                ["Dashboard:AdminKey"] = "test-admin-key",
                ["Security:SecureCookies"] = "false",
                ["Telemetry:IngestionRateLimitPerMinute"] = "3",
                ["Telemetry:LoginRateLimitPerMinute"] = "100",
                ["Telemetry:MaintenanceInitialDelaySeconds"] = "3600",
                ["Telemetry:MaintenanceIntervalHours"] = "24"
            }));
        return base.CreateHost(builder);
    }
}
