using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Demo;

public static class MyDashboardAuthenticationSchemeDefaults
{
    public const string Scheme = "MyDashboardAuthenticationScheme";
}

public class MyDashboardAuthenticationSchemeOptions : AuthenticationSchemeOptions { }

public class MyDashboardAuthenticationHandler(
    IOptionsMonitor<MyDashboardAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<MyDashboardAuthenticationSchemeOptions>(options, logger, encoder)
{
    // options.CurrentValue.ForwardChallenge = "";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var testAuthHeaderPresent = Request.Headers["X-Base-Token"].Contains("xxx", StringComparer.Ordinal);

        var authResult = testAuthHeaderPresent ? _CreateAuthenticationTicket() : AuthenticateResult.NoResult();

        return Task.FromResult(authResult);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        //Response.Headers["WWW-Authenticate"] = MyDashboardAuthenticationSchemeDefaults.Scheme;
        //return base.HandleChallengeAsync(properties);

        // Challenge use OpenId for AddCapWithOpenIdAndCustomAuthorization
        return Context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
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
