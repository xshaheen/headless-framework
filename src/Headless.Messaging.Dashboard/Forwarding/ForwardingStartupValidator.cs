// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Fails the host at start when forwarding is configured so that a peer's call would be admitted by something weaker
/// than the forwarding check: an open dashboard, a policy a signed-in user already passes, or no policy at all.
/// </summary>
/// <remarks>
/// The key's length is checked by the options validator; its message never quotes the key.
/// </remarks>
internal sealed class ForwardingStartupValidator(
    IOptions<MessagingDashboardOptions> module,
    IOptionsMonitor<AuthConfig> auth,
    IAuthorizationPolicyProvider policies,
    IServiceProvider services
) : IHeadlessStartupValidator
{
    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        var options = module.Value;
        var forwarding = options.Forwarding;
        var standalone = services.GetService<MessagingStandaloneMarker>() is not null;

        if (standalone && !forwarding.IsEnabled)
        {
            throw new InvalidOperationException(
                "The dashboard-only Messaging host (AddMessagingStandalone) reads no store of its own, so it needs "
                    + "forwarding: set Forwarding.Key or Forwarding.Policy."
            );
        }

        if (standalone && services.GetService<INodeDiscoveryProvider>() is null)
        {
            throw new InvalidOperationException(
                "The dashboard-only Messaging host (AddMessagingStandalone) finds its nodes through service discovery: "
                    + "register a discovery provider, such as the Kubernetes or Consul one."
            );
        }

        if (!forwarding.IsEnabled)
        {
            return;
        }

        var config = auth.Get(SetupDashboard.AuthenticationName);

        // An open dashboard admits everyone, so a forwarded call's operator name and access would be taken on trust.
        if (config.Mode == AuthMode.None)
        {
            throw new InvalidOperationException(
                "Messaging forwarding cannot run under WithNoAuth(): a node would accept the operator and access any "
                    + "caller names. Configure dashboard authentication, or turn forwarding off."
            );
        }

        if (forwarding.Policy is not { } name)
        {
            return;
        }

        // A policy a signed-in user passes would let that user's browser call the node-to-node surface as a peer.
        string?[] dashboardPolicies =
        [
            options.ViewPolicy,
            options.OperatePolicy,
            config.Mode == AuthMode.Host ? config.Host.ViewPolicy : null,
            config.Mode == AuthMode.Host ? config.Host.OperatePolicy : null,
        ];

        if (dashboardPolicies.Any(policy => string.Equals(policy, name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Forwarding.Policy '{name}' is also a view or operate policy of the dashboard. Name a policy that "
                    + "admits only the deployment's service identity."
            );
        }

        var policy =
            await policies.GetPolicyAsync(name).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Forwarding.Policy names the authorization policy '{name}', which the host does not register."
            );

        // Without its own schemes the policy would judge whatever the request's default authentication signed in,
        // such as an operator's cookie, and the forwarding scheme itself would evaluate the policy forever.
        if (policy.AuthenticationSchemes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Forwarding.Policy '{name}' names no authentication scheme. Add the host's service-identity scheme, "
                    + "such as a client-certificate or workload-token scheme, with AddAuthenticationSchemes."
            );
        }

        if (policy.AuthenticationSchemes.Contains(ForwardingProtocol.Scheme, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Forwarding.Policy '{name}' authenticates through the '{ForwardingProtocol.Scheme}' scheme, which "
                    + "evaluates that policy. Name the host's own service-identity scheme instead."
            );
        }
    }
}
