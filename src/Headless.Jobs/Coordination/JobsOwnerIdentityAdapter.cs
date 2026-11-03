// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;

namespace Headless.Jobs.Coordination;

/// <summary>
/// Coordinated owner identity for the durable path: renders the current membership identity as the
/// cluster-qualified <c>cluster/node@incarnation</c> owner tag (<see cref="NodeOwnerTag"/>) and surfaces the local
/// membership-loss token.
/// </summary>
/// <remarks>
/// Keeps the full <c>Headless.Coordination.Abstractions</c> dependency out of the low-level EF persistence
/// layer — the persistence provider and instrumentation depend only on <see cref="IJobsOwnerIdentity"/>,
/// while this single adapter wraps <see cref="INodeMembership"/>. Centralizes the
/// "not-registered / membership-lost" policy so it is not scattered across the stamp sites.
/// </remarks>
internal sealed class JobsOwnerIdentityAdapter(INodeMembership membership, SchedulerOptionsBuilder optionsBuilder)
    : IJobsOwnerIdentity
{
    public string DisplayOwner => membership.GetOwnerTag() ?? optionsBuilder.NodeId;

    public bool TryGetStampOwner([NotNullWhen(true)] out string? owner)
    {
        owner = membership.GetOwnerTag();

        return owner is not null;
    }

    public CancellationToken MembershipLostToken => membership.LocalMembershipLostToken;
}
