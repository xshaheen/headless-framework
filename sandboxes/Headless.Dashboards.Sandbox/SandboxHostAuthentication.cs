// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Headless.Dashboards.Sandbox;

/// <summary>
/// A stand-in for an application's own sign-in, so the Jobs dashboard's host mode can be walked without an identity
/// provider. Not a pattern for production: it compares one shared secret.
/// </summary>
/// <remarks>
/// Like an application that signs its SPA in with a bearer token, it has no sign-in redirect: a browser gets a
/// dashboard session by posting the token to the dashboard's session exchange, which the <c>/sandbox/sign-in</c> page
/// does. The viewer role may view the dashboard, and only the operator role may run its write actions.
/// </remarks>
internal static class SandboxHostAuthentication
{
    public const string Scheme = "SandboxHost";
    public const string ViewPolicy = "SandboxViewers";
    public const string OperatePolicy = "SandboxOperators";
    public const string OperatorRole = "operator";
    public const string ViewerRole = "viewer";
    public const string ViewerPrefix = "viewer-";

    public static void Add(IServiceCollection services, string secret)
    {
        services
            .AddAuthentication(Scheme)
            .AddScheme<SandboxHostOptions, SandboxHostHandler>(Scheme, options => options.Secret = secret);
        services
            .AddAuthorizationBuilder()
            .AddPolicy(ViewPolicy, policy => policy.RequireRole(ViewerRole, OperatorRole))
            .AddPolicy(OperatePolicy, policy => policy.RequireRole(OperatorRole));
    }

    /// <summary>
    /// Maps <c>GET /sandbox/sign-in</c>, a page that exchanges a sandbox token for a Jobs dashboard session the way an
    /// application's SPA would: it posts the token as a bearer credential to <c>api/auth/session</c>, then opens the
    /// dashboard.
    /// </summary>
    public static void MapSignInPage(IEndpointRouteBuilder endpoints, string dashboardPath)
    {
        var page = _SignInPage.Replace("{dashboard}", dashboardPath, StringComparison.Ordinal);
        endpoints.MapGet("/sandbox/sign-in", () => Results.Content(page, "text/html; charset=utf-8"));
    }

    private const string _SignInPage = """
        <!doctype html>
        <meta charset="utf-8">
        <title>Sandbox host sign-in</title>
        <form id="f">
          <p>Sandbox token: <code>&lt;secret&gt;</code> signs in an operator, <code>viewer-&lt;secret&gt;</code> a viewer.</p>
          <input id="t" type="password" size="40" autofocus>
          <button>Sign in to the Jobs dashboard</button>
          <p id="e"></p>
        </form>
        <script>
          document.getElementById('f').addEventListener('submit', async (event) => {
            event.preventDefault();
            const res = await fetch('{dashboard}/api/auth/session', {
              method: 'POST',
              headers: {
                Authorization: 'Bearer ' + document.getElementById('t').value,
                'X-Requested-With': 'XMLHttpRequest',
              },
            });
            if (res.status === 204) {
              location.assign('{dashboard}/');
            } else {
              document.getElementById('e').textContent = 'Session exchange answered ' + res.status;
            }
          });
        </script>
        """;

    internal sealed class SandboxHostOptions : AuthenticationSchemeOptions
    {
        public string Secret { get; set; } = "";
    }

    /// <summary>
    /// Signs in <c>Authorization: Bearer &lt;secret&gt;</c> as <c>sandbox-operator</c> in the operator role, and
    /// <c>Bearer viewer-&lt;secret&gt;</c> as <c>sandbox-viewer</c> in the viewer role; anything else is no result.
    /// </summary>
    internal sealed class SandboxHostHandler(
        IOptionsMonitor<SandboxHostOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<SandboxHostOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var token = header["Bearer ".Length..];
            var isViewer = token.StartsWith(ViewerPrefix, StringComparison.Ordinal);
            var secret = isViewer ? token[ViewerPrefix.Length..] : token;

            if (
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(secret),
                    Encoding.UTF8.GetBytes(Options.Secret)
                )
            )
            {
                return Task.FromResult(AuthenticateResult.Fail("Unknown sandbox host token."));
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, isViewer ? "sandbox-viewer" : "sandbox-operator"),
                    new Claim(ClaimTypes.Role, isViewer ? ViewerRole : OperatorRole),
                ],
                SandboxHostAuthentication.Scheme
            );

            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(new(identity), SandboxHostAuthentication.Scheme))
            );
        }
    }
}
