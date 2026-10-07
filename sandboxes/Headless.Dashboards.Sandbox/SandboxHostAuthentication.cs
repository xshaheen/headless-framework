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
internal static class SandboxHostAuthentication
{
    public const string Scheme = "SandboxHost";
    public const string Policy = "SandboxOperators";
    public const string OperatorRole = "operator";
    public const string ViewerPrefix = "viewer-";

    public static void Add(IServiceCollection services, string secret)
    {
        services
            .AddAuthentication(Scheme)
            .AddScheme<SandboxHostOptions, SandboxHostHandler>(Scheme, options => options.Secret = secret);
        services.AddAuthorizationBuilder().AddPolicy(Policy, policy => policy.RequireRole(OperatorRole));
    }

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
                    new Claim(ClaimTypes.Role, isViewer ? "viewer" : OperatorRole),
                ],
                SandboxHostAuthentication.Scheme
            );

            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(new(identity), SandboxHostAuthentication.Scheme))
            );
        }
    }
}
