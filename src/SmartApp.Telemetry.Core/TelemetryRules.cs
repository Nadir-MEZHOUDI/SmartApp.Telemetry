using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartApp.Telemetry.Core;

public static class TelemetryRules
{
    public static readonly string[] AllowedEventNames =
    [
        "app_first_started", "app_started", "app_closed", "feature_used",
        "operation_completed", "operation_failed", "update_available", "update_started",
        "update_completed", "update_failed", "exception", "fatal_exception"
    ];

    public static string? ValidateEvent(TelemetryEventRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Application) || request.Application.Length > 100)
            return "Application is required and must be at most 100 characters.";
        if (request.InstallationId == Guid.Empty)
            return "InstallationId is required.";
        if (string.IsNullOrWhiteSpace(request.EventName) || request.EventName.Length > 100)
            return "EventName is required and must be at most 100 characters.";
        if (!AllowedEventNames.Contains(request.EventName, StringComparer.Ordinal))
            return "The event name is not allowed.";
        if (request.Properties is { } properties && properties.ValueKind is not JsonValueKind.Object and not JsonValueKind.Null)
            return "Properties must be a JSON object.";
        return null;
    }

    public static string? ValidateException(ExceptionTelemetryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Application) || request.Application.Length > 100)
            return "Application is required and must be at most 100 characters.";
        if (request.InstallationId == Guid.Empty)
            return "InstallationId is required.";
        if (string.IsNullOrWhiteSpace(request.ExceptionType) || request.ExceptionType.Length > 300)
            return "ExceptionType is required and must be at most 300 characters.";
        if (request.AdditionalContext is { } context && context.ValueKind is not JsonValueKind.Object and not JsonValueKind.Null)
            return "AdditionalContext must be a JSON object.";
        return null;
    }

    public static string Fingerprint(string exceptionType, string? stackTrace)
    {
        var frames = (stackTrace ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.TrimStart().StartsWith("at ", StringComparison.Ordinal))
            .Select(line => Regex.Replace(line, @":line\s+\d+", string.Empty, RegexOptions.IgnoreCase))
            .Take(8);
        var source = $"{exceptionType}\n{string.Join('\n', frames)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }

    public static string Sanitise(string? value, int maxLength = 10_000)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = value;
        var patterns = new[]
        {
            @"(?i)(password|pwd)\s*=\s*[^;\s]+",
            @"(?i)(api[-_ ]?key|token|secret)\s*[:=]\s*[^;\s]+",
            @"(?i)bearer\s+[A-Za-z0-9._~+/=-]+",
            @"(?i)authorization\s*[:=]\s*[^\r\n]+"
        };
        foreach (var pattern in patterns)
            result = Regex.Replace(result, pattern, "[REDACTED]", RegexOptions.CultureInvariant);
        return result.Length <= maxLength ? result : result[..maxLength];
    }
}
