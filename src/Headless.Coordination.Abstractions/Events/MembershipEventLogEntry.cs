// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Represents one membership event this process published, as <see cref="IMembershipEventLog"/> keeps it.</summary>
/// <param name="Sequence">The event's position in this process's log: unique, gap-free, and starting at 1.</param>
/// <param name="Event">The published membership event.</param>
/// <param name="ObservedAt">When this process published the event, from the registered <see cref="TimeProvider"/>.</param>
/// <param name="State">
/// The liveness state of the snapshot the event came from, or <see langword="null"/> for a node that disappeared from
/// the snapshot and for <see cref="LocalMembershipLost"/>.
/// </param>
/// <param name="LastHeartbeatAt">
/// The store-clock last heartbeat of the snapshot the event came from, or <see langword="null"/> when
/// <paramref name="State"/> is <see langword="null"/>.
/// </param>
/// <param name="IsFirstObservation">
/// <see langword="true"/> when the event came from the first time this process saw the incarnation, such as every
/// node already in the store at the process's first beat: it reports existing state, not a change at
/// <paramref name="ObservedAt"/>.
/// </param>
[PublicAPI]
public sealed record MembershipEventLogEntry(
    long Sequence,
    NodeMembershipEvent Event,
    DateTimeOffset ObservedAt,
    NodeLivenessState? State,
    DateTimeOffset? LastHeartbeatAt,
    bool IsFirstObservation
);
