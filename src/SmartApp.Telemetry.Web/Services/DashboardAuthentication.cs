using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace SmartApp.Telemetry.Web.Services;

public static class DashboardAuthentication
{
    public static bool PasswordMatches(string? configuredPassword, string? suppliedPassword)
    {
        if (string.IsNullOrEmpty(configuredPassword) || suppliedPassword is null)
            return false;

        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredPassword));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedPassword));
        return CryptographicOperations.FixedTimeEquals(configuredHash, suppliedHash);
    }

    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) &&
        returnUrl.StartsWith('/') &&
        !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/";

    public static string LoginUrl(string? returnUrl, string? error = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["returnUrl"] = SafeReturnUrl(returnUrl)
        };
        if (!string.IsNullOrWhiteSpace(error))
            values["error"] = error;
        return QueryHelpers.AddQueryString("/login", values);
    }
}
