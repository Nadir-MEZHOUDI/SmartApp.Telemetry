using System.Text.Json;

namespace SmartApp.Telemetry.Client;

internal sealed record TelemetryContext(
    string? AppVersion,
    string? OperatingSystem,
    string? OperatingSystemVersion,
    string? Architecture,
    string? Language);

internal sealed record TelemetryEventRequest(
    string Application,
    Guid InstallationId,
    string EventName,
    DateTimeOffset Timestamp,
    TelemetryContext? Context,
    JsonElement? Properties);

internal sealed record ExceptionTelemetryRequest(
    string Application,
    Guid InstallationId,
    string ExceptionType,
    string? Message,
    string? StackTrace,
    DateTimeOffset Timestamp,
    TelemetryContext? Context,
    JsonElement? AdditionalContext);
