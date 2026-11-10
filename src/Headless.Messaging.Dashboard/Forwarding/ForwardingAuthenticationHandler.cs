// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Headless.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The <c>HeadlessDashboardForwarding</c> scheme: admits a call another Messaging node forwarded, by the shared key or
/// by the host's forwarding policy, and only then reads the operator and the access the serving node sent.
/// </summary>
/// <remarks>
/// <para>
/// A request without the hop header gets no result, so a browser's own session still decides for it. A forwarded call
/// with a missing or wrong key fails (<c>401</c>, refusal <c>key-rejected</c>). Under a host policy, a node the
/// policy's schemes do not authenticate fails too; a node they authenticate but the policy denies gets an identity
/// without the admitted claim, so the forwarding policy refuses it (<c>403</c>, refusal <c>policy-denied</c>). Neither
/// reaches a store.
/// </para>
/// <para>
/// The admitted identity names the operator <c>{actor} via {serving node}</c> and carries the lower of the serving
/// node's decision and this node's <see cref="MessagingDashboardOptions.ForwardedAccess"/>. A placeholder or missing
/// actor gets no name, so the forwarding policy refuses its writes.
/// </para>
/// </remarks>
internal sealed class ForwardingAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<MessagingDashboardOptions> module,
    IOptions<HeadlessDashboardOptions> dashboard
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    // Bounded so that "<operator> via <node>" stays a short ledger and log field whatever a caller sends.
    private const int _MaxActorLength = 128;
    private const int _MaxHopLength = 64;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var forwarding = module.Value.Forwarding;

        if (!forwarding.IsEnabled || !_IsModuleRequest())
        {
            return AuthenticateResult.NoResult();
        }

        var hop = Request.Headers[ForwardingProtocol.HopHeader].ToString();

        if (hop.Length == 0 && !Request.Headers.ContainsKey(ForwardingProtocol.KeyHeader))
        {
            return AuthenticateResult.NoResult();
        }

        if (!_IsName(hop, _MaxHopLength))
        {
            return AuthenticateResult.Fail("A forwarded call must name its serving node in the hop header.");
        }

        if (forwarding.Key is not null)
        {
            // The failure message never carries either value: it reaches the host's authentication logs.
            if (!KeysMatch(Request.Headers[ForwardingProtocol.KeyHeader].ToString(), forwarding.Key))
            {
                return AuthenticateResult.Fail("The forwarding key is missing or wrong.");
            }
        }
        else
        {
            var admitted = await _EvaluateHostPolicyAsync(forwarding.Policy!).ConfigureAwait(false);

            if (admitted is null)
            {
                return AuthenticateResult.Fail("The calling node did not authenticate for the forwarding policy.");
            }

            if (admitted == false)
            {
                // Authenticated but denied: an identity without the admitted claim makes the forwarding policy forbid
                // the call, so the serving node tells "policy denied" from "not authenticated".
                var denied = new ClaimsIdentity([new Claim(ForwardingProtocol.FromClaim, hop)], Scheme.Name);
                return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(denied), Scheme.Name));
            }
        }

        return AuthenticateResult.Success(new AuthenticationTicket(_Admit(hop), Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var result = await HandleAuthenticateOnceSafeAsync().ConfigureAwait(false);

        if (result.Failure is not null)
        {
            Response.Headers[ForwardingProtocol.RefusalHeader] = module.Value.Forwarding.Key is not null
                ? ForwardingProtocol.RefusalKey
                : ForwardingProtocol.RefusalPolicy;
        }

        Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        var result = await HandleAuthenticateOnceSafeAsync().ConfigureAwait(false);

        if (result.Succeeded)
        {
            Response.Headers[ForwardingProtocol.RefusalHeader] = result.Principal.HasClaim(static claim =>
                string.Equals(claim.Type, ForwardingProtocol.AdmittedClaim, StringComparison.Ordinal)
            )
                ? ForwardingProtocol.RefusalAccess
                : ForwardingProtocol.RefusalPolicy;
        }

        Response.StatusCode = StatusCodes.Status403Forbidden;
    }

    /// <summary>Compares two keys in time that does not depend on where they differ, or on their lengths.</summary>
    internal static bool KeysMatch(string presented, string expected)
    {
        // Hashing first gives both sides one length, so the comparison leaks neither the key's length nor the first
        // differing character.
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(expected));

        return CryptographicOperations.FixedTimeEquals(left, right) && presented.Length > 0;
    }

    private bool _IsModuleRequest()
    {
        var basePath = dashboard.Value.BasePath.TrimEnd('/');

        return Request.Path.StartsWithSegments(
            basePath + "/api/" + MessagingDashboardOptions.ModuleId,
            StringComparison.OrdinalIgnoreCase
        );
    }

    // True when the host policy admits the node, false when its schemes authenticate the node but it denies it, and
    // null when they do not authenticate it.
    private async Task<bool?> _EvaluateHostPolicyAsync(string policyName)
    {
        var provider = Context.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>();
        var evaluator = Context.RequestServices.GetRequiredService<IPolicyEvaluator>();
        var policy = await provider.GetPolicyAsync(policyName).ConfigureAwait(false);

        if (policy is null)
        {
            return null;
        }

        // The evaluator replaces the request's user with the node's principal; the dashboard policy that called this
        // scheme sets its own user afterwards, so the original is put back rather than leaked.
        var user = Context.User;

        try
        {
            var authentication = await evaluator.AuthenticateAsync(policy, Context).ConfigureAwait(false);

            if (!authentication.Succeeded)
            {
                return null;
            }

            var authorization = await evaluator
                .AuthorizeAsync(policy, authentication, Context, resource: Context)
                .ConfigureAwait(false);

            return authorization.Succeeded;
        }
        finally
        {
            Context.User = user;
        }
    }

    private ClaimsPrincipal _Admit(string hop)
    {
        var moduleOptions = module.Value;
        var asserted = string.Equals(
            Request.Headers[ForwardingProtocol.AccessHeader].ToString(),
            ForwardingProtocol.AccessOperate,
            StringComparison.Ordinal
        )
            ? MessagingForwardedAccess.Operate
            : MessagingForwardedAccess.View;
        var access = asserted < moduleOptions.ForwardedAccess ? asserted : moduleOptions.ForwardedAccess;

        var claims = new List<Claim>
        {
            new(ForwardingProtocol.AdmittedClaim, "true"),
            new(ForwardingProtocol.FromClaim, hop),
            new(
                ForwardingProtocol.AccessClaim,
                access == MessagingForwardedAccess.Operate
                    ? ForwardingProtocol.AccessOperate
                    : ForwardingProtocol.AccessView
            ),
        };

        var actor = _Actor();

        if (actor is not null)
        {
            var name = actor + " via " + hop;
            claims.Add(new Claim(ClaimTypes.NameIdentifier, name));
            claims.Add(new Claim(ClaimTypes.Name, name));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
    }

    private string? _Actor()
    {
        var raw = Request.Headers[ForwardingProtocol.ActorHeader].ToString();

        if (raw.Length == 0)
        {
            return null;
        }

        string actor;

        try
        {
            actor = Uri.UnescapeDataString(raw);
        }
        catch (UriFormatException)
        {
            return null;
        }

        // A placeholder names nobody, so the ledger could not say who ran a command; such a call stays read-only.
        if (
            !_IsName(actor, _MaxActorLength, allowSpaces: true)
            || string.Equals(actor, "anonymous", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actor, "host-user", StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        return actor;
    }

    // Control characters would let a forged name break a log or ledger line.
    private static bool _IsName(string value, int maxLength, bool allowSpaces = false)
    {
        return value.Length > 0
            && value.Length <= maxLength
            && !string.IsNullOrWhiteSpace(value)
            && !value.Any(c => char.IsControl(c) || (!allowSpaces && char.IsWhiteSpace(c)));
    }
}
