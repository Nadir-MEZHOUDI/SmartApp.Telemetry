var builder = WebApplication.CreateBuilder(args);
var apiBaseUrl = builder.Configuration["TelemetryApi:BaseUrl"] ?? "http://localhost:5000";
var adminKey = builder.Configuration["TelemetryApi:AdminKey"] ?? string.Empty;

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/config.js", () => Results.Text(
    $"window.telemetryConfig = {{ apiBaseUrl: {System.Text.Json.JsonSerializer.Serialize(apiBaseUrl.TrimEnd('/'))}, adminKey: {System.Text.Json.JsonSerializer.Serialize(adminKey)} }};",
    "application/javascript"));

app.MapFallbackToFile("index.html");
app.Run();
