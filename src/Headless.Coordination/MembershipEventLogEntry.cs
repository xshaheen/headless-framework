// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Represents one membership event this process published, as <see cref="IMembershipEventLog"/> keeps it.</summary>
[PublicAPI]
public sealed record MembershipEventLogEntry
{
    /// <summary>Gets the event's position in this process's log: unique, gap-free, and starting at 1.</summary>
    public required long Sequence { get; init; }

    /// <summary>Gets the published membership event.</summary>
    public required NodeMembershipEvent Event { get; init; }

    /// <summary>Gets when this process published the event, from the registered <see cref="TimeProvider"/>.</summary>
    public required DateTimeOffset ObservedAt { get; init; }

    /// <summary>
    /// Gets the liveness state of the snapshot the event came from, or <see langword="null"/> for a node that
    /// disappeared from the snapshot and for <see cref="LocalMembershipLost"/>.
    /// </summary>
    public required NodeLivenessState? State { get; init; }

    /// <summary>
    /// Gets the store-clock last heartbeat of the snapshot the event came from, or <see langword="null"/> when
    /// <see cref="State"/> is <see langword="null"/>.
    /// </summary>
    public required DateTimeOffset? LastHeartbeatAt { get; init; }

    /// <summary>
    /// Gets whether the event came from the first time this process saw the incarnation, such as every node already
    /// in the store at the process's first beat: it reports existing state, not a change at <see cref="ObservedAt"/>.
    /// </summary>
    public required bool IsFirstObservation { get; init; }
}
