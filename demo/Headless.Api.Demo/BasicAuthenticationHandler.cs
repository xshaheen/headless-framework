using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Api.Security.Claims;
using Headless.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Demo;

internal sealed class BasicAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IClaimsPrincipalFactory claimsPrincipalFactory
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string _UserName = "test";
    private const string _Password = "p@ssw0rd";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Context.Request.Headers.Authorization.ToString();

        if (
            string.IsNullOrEmpty(authorization)
            || !authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
        )
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var encodedCredentials = authorization["Basic ".Length..].Trim();

        string credentialString;

        try
        {
            var credentialBytes = Convert.FromBase64String(encodedCredentials);
            credentialString = Encoding.UTF8.GetString(credentialBytes);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Failed to decode Basic authentication credentials");
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization header"));
        }

        var parts = credentialString.Split(':', 2);
        if (parts.Length != 2)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid Basic credentials"));
        }

        var username = parts[0];
        var password = parts[1];

        if (
            !string.Equals(username, _UserName, StringComparison.Ordinal)
            || !string.Equals(password, _Password, StringComparison.Ordinal)
        )
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid username or password"));
        }

        var principal = claimsPrincipalFactory.CreateClaimsPrincipal(new Claim(UserClaimTypes.Name, username));
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Basic realm=\"\", charset=\"UTF-8\"";
        return base.HandleChallengeAsync(properties);
    }
}
