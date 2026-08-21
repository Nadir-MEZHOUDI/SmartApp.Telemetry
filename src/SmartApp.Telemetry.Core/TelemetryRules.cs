using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartApp.Telemetry.Core;

public static class TelemetryRules
{
    private const int MaxProperties = 30;
    private const int MaxPropertyKeyLength = 100;
    private const int MaxStringLength = 2_000;
    private const int MaxArrayItems = 100;
    private const int MaxDepth = 6;

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
        if (!IsAllowedOrCustomEventName(request.EventName))
            return "The event name is not allowed. Use a known name (app_started, feature_used, etc.) or a custom lowercase name like 'user_action' matching ^[a-z][a-z0-9_.-]*$.";
        return ValidateProperties(request.Properties, "Properties");
    }

    private static bool IsAllowedOrCustomEventName(string name)
    {
        if (AllowedEventNames.Contains(name, StringComparer.Ordinal)) return true;
        // Allow custom analytics events: 2-100 chars, starts with letter, only a-z0-9 _ . -
        return Regex.IsMatch(name, @"^[a-z][a-z0-9_\.\-]{1,99}$");
    }

    public static string? ValidateException(ExceptionTelemetryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Application) || request.Application.Length > 100)
            return "Application is required and must be at most 100 characters.";
        if (request.InstallationId == Guid.Empty)
            return "InstallationId is required.";
        if (string.IsNullOrWhiteSpace(request.ExceptionType) || request.ExceptionType.Length > 300)
            return "ExceptionType is required and must be at most 300 characters.";
        return ValidateProperties(request.AdditionalContext, "AdditionalContext");
    }

    public static string? ValidateProperties(JsonElement? properties, string label)
    {
        if (properties is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (element.ValueKind != JsonValueKind.Object)
            return $"{label} must be a JSON object.";
        if (element.EnumerateObject().Count() > MaxProperties)
            return $"{label} may contain at most {MaxProperties} entries.";
        return ValidateValue(element, 0, label);
    }

    private static string? ValidateValue(JsonElement value, int depth, string label)
    {
        if (depth > MaxDepth)
            return $"{label} are nested too deeply.";
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                if (value.EnumerateObject().Count() > MaxProperties)
                    return $"{label} objects may contain at most {MaxProperties} entries.";
                foreach (var property in value.EnumerateObject())
                {
                    if (property.Name.Length > MaxPropertyKeyLength)
                        return $"{label} keys must be at most {MaxPropertyKeyLength} characters.";
                    var error = ValidateValue(property.Value, depth + 1, label);
                    if (error is not null) return error;
                }
                return null;
            case JsonValueKind.Array:
                if (value.GetArrayLength() > MaxArrayItems)
                    return $"{label} arrays may contain at most {MaxArrayItems} items.";
                foreach (var item in value.EnumerateArray())
                {
                    var error = ValidateValue(item, depth + 1, label);
                    if (error is not null) return error;
                }
                return null;
            case JsonValueKind.String:
                return value.GetString() is { Length: > MaxStringLength }
                    ? $"String values in {label} must be at most {MaxStringLength} characters."
                    : null;
            default:
                return null;
        }
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
