using SmartApp.Telemetry.Web.Services;
using Xunit;

namespace SmartApp.Telemetry.Web.Tests;

public sealed class DashboardAuthenticationTests
{
    [Fact]
    public void PasswordMatches_accepts_only_the_configured_password()
    {
        Assert.True(DashboardAuthentication.PasswordMatches("correct horse", "correct horse"));
        Assert.False(DashboardAuthentication.PasswordMatches("correct horse", "wrong horse"));
        Assert.False(DashboardAuthentication.PasswordMatches(null, "correct horse"));
        Assert.False(DashboardAuthentication.PasswordMatches("correct horse", null));
    }

    [Theory]
    [InlineData("/errors", "/errors")]
    [InlineData("/errors?status=open", "/errors?status=open")]
    [InlineData("https://attacker.example", "/")]
    [InlineData("//attacker.example", "/")]
    [InlineData(null, "/")]
    public void SafeReturnUrl_rejects_external_redirects(string? value, string expected)
    {
        Assert.Equal(expected, DashboardAuthentication.SafeReturnUrl(value));
    }
}
