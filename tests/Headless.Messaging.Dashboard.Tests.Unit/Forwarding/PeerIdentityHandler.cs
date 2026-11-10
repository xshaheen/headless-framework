// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Forwarding;

/// <summary>
/// A host's own service identity for node-to-node calls: <c>X-Test-Peer: {name}</c> signs in as that node, in the
/// <c>peer</c> role only when it is one of the deployment's nodes.
/// </summary>
internal sealed class PeerIdentityHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestPeer";
    public const string PolicyName = "PeerNodes";
    public const string Header = "X-Test-Peer";

    /// <summary>Registers the scheme and the <c>PeerNodes</c> policy that admits only the deployment's nodes.</summary>
    public static void AddTo(IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, PeerIdentityHandler>(SchemeName, _ => { });
        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                PolicyName,
                policy => policy.AddAuthenticationSchemes(SchemeName).RequireAuthenticatedUser().RequireRole("peer")
            );
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var name = Request.Headers[Header].ToString();

        if (name.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        List<Claim> claims = [new(ClaimTypes.Name, name)];

        if (name.StartsWith("node-", StringComparison.Ordinal))
        {
            claims.Add(new Claim(ClaimTypes.Role, "peer"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new(identity), SchemeName)));
    }
}
