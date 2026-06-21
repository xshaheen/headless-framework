// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Interfaces;

namespace Headless.Jobs.Coordination;

/// <summary>
/// Default owner identity for the zero-infra in-memory path. The owner is the configured node identifier
/// (machine name by default); the node is always considered registered and membership is never lost.
/// </summary>
/// <remarks>
/// Registered via <c>TryAddSingleton</c> so the in-memory single-process path and the always-on
/// instrumentation can resolve <see cref="IJobsOwnerIdentity"/> without a coordination provider. The
/// durable path overrides this with a coordinated adapter over the membership substrate.
/// </remarks>
internal sealed class DefaultJobsOwnerIdentity(SchedulerOptionsBuilder optionsBuilder) : IJobsOwnerIdentity
{
    public string DisplayOwner => optionsBuilder.NodeId;

    public bool TryGetStampOwner([NotNullWhen(true)] out string? owner)
    {
        owner = optionsBuilder.NodeId;

        return true;
    }

    public CancellationToken MembershipLostToken => CancellationToken.None;
}
