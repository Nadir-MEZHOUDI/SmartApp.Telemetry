using System.Security.Cryptography;
using System.Text;

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
        returnUrl.StartsWith("/", StringComparison.Ordinal) &&
        !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
