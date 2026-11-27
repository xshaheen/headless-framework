// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination;
using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Cluster;

/// <summary>
/// The two halves the module reads, as the host registered them: Coordination, and the health checks with their
/// monitor. Each half is optional, so the module serves whichever the host has.
/// </summary>
/// <remarks>
/// Coordination counts as present only when its cluster summary resolves. Messaging registers a do-nothing node
/// membership by default, so a resolvable membership alone says nothing about whether a real provider runs.
/// </remarks>
internal sealed class ClusterServices
{
    public ClusterServices(IServiceProvider provider)
    {
        Argument.IsNotNull(provider);

        Summary = provider.GetService<CoordinationClusterSummary>();

        if (Summary is not null)
        {
            Membership = provider.GetService<INodeMembership>();
            EventLog = provider.GetService<IMembershipEventLog>();
        }

        HasHealthChecks = provider.GetService<HealthCheckService>() is not null;
        Monitor = provider.GetService<IHealthCheckMonitor>();
    }

    /// <summary>Gets the coordination settings, or <see langword="null"/> when the host runs no Coordination.</summary>
    public CoordinationClusterSummary? Summary { get; }

    /// <summary>Gets the node membership when Coordination is present.</summary>
    public INodeMembership? Membership { get; }

    /// <summary>Gets this node's membership event log when Coordination is present.</summary>
    public IMembershipEventLog? EventLog { get; }

    /// <summary>Gets whether the host registered health checks.</summary>
    public bool HasHealthChecks { get; }

    /// <summary>Gets the health check monitor, or <see langword="null"/> when the host did not register one.</summary>
    public IHealthCheckMonitor? Monitor { get; }

    /// <summary>Gets whether the host serves the Coordination half.</summary>
    [MemberNotNullWhen(true, nameof(Summary), nameof(Membership))]
    public bool HasCoordination => Summary is not null && Membership is not null;

    /// <summary>Gets the serving node's identity, or <see langword="null"/> when it is not a registered member.</summary>
    public NodeIdentity? LocalIdentity => HasCoordination ? Membership.Identity : null;

    /// <summary>
    /// Gets the label of the host serving this request: its node identity when it is a member, else its machine name,
    /// so an operator behind a load balancer can tell which replica answered.
    /// </summary>
    public string HostLabel => LocalIdentity?.ToString() ?? Environment.MachineName;
}
