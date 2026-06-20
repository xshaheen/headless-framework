// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs.Interfaces;

namespace Headless.Jobs.Coordination;

/// <summary>
/// Coordinated owner identity for the durable path: renders the current membership identity as the
/// <c>node@incarnation</c> owner string and surfaces the local membership-loss token.
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
    public string DisplayOwner => membership.Identity?.ToString() ?? optionsBuilder.NodeId;

    public bool TryGetStampOwner([NotNullWhen(true)] out string? owner)
    {
        if (membership.Identity is { } identity)
        {
            owner = identity.ToString();

            return true;
        }

        owner = null;

        return false;
    }

    public CancellationToken MembershipLostToken => membership.LocalMembershipLostToken;
}
