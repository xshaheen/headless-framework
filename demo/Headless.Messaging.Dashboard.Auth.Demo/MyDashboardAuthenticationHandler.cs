using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Demo;

public static class MyDashboardAuthenticationSchemeDefaults
{
    public const string Scheme = "MyDashboardAuthenticationScheme";
}

public class MyDashboardAuthenticationSchemeOptions : AuthenticationSchemeOptions;

public class MyDashboardAuthenticationHandler(
    IOptionsMonitor<MyDashboardAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<MyDashboardAuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var testAuthHeaderPresent = Request.Headers["X-Base-Token"].Contains("xxx", StringComparer.Ordinal);

        var authResult = testAuthHeaderPresent ? _CreateAuthenticationTicket() : AuthenticateResult.NoResult();

        return Task.FromResult(authResult);
    }

    private static AuthenticateResult _CreateAuthenticationTicket()
    {
        var claims = new[] { new Claim(ClaimTypes.Name, "My Dashboard user") };
        var identity = new ClaimsIdentity(claims, MyDashboardAuthenticationSchemeDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, MyDashboardAuthenticationSchemeDefaults.Scheme);

        return AuthenticateResult.Success(ticket);
    }
}
