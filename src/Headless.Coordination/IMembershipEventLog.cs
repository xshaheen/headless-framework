// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Reads the bounded log of membership events this process published, for display and diagnostics.</summary>
/// <remarks>
/// The log is a local view: it holds what this node observed, stamped with this node's clock, not a cluster-wide
/// history. It is not a recovery path. Consumers that act on membership reconcile from
/// <see cref="INodeMembership.GetLivenessSnapshotAsync"/>.
/// </remarks>
[PublicAPI]
public interface IMembershipEventLog
{
    /// <summary>Returns the most recent membership events this process published, oldest first.</summary>
    /// <returns>
    /// A copy of the retained entries, at most the last 256. Empty on a host that runs no membership heartbeat and
    /// whose membership was never lost.
    /// </returns>
    /// <remarks>
    /// <see cref="MembershipEventLogEntry.Sequence"/> increases by one per published event and starts at 1 in each
    /// process. A poller treats an entry above its last seen sequence as new, and a gap between its last seen sequence
    /// and the oldest returned one as evicted entries. Sequences restart when the process restarts, so a poller that
    /// sees a newest sequence below its cursor resets it.
    /// </remarks>
    IReadOnlyList<MembershipEventLogEntry> GetEntries();
}
