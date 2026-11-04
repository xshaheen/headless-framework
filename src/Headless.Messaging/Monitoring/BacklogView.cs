// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// How much stored work is still waiting, returned by <see cref="IMonitoringApi.GetBacklogAsync"/>: per table, the
/// number of pending rows and how long the oldest of them has been waiting.
/// </summary>
/// <remarks>
/// <para>
/// A row is pending when it is neither terminal nor scheduled for later: its status is <c>Scheduled</c>, a
/// <c>Delayed</c> or <c>Queued</c> row whose publish time has come, or <c>Failed</c> with a persisted retry still due.
/// A published row that spent its inline retries against an unreachable broker is stored <c>Failed</c> with a next
/// retry, so a broker outage shows as a growing published backlog. A message published for a later time is not
/// pending until that time.
/// </para>
/// <para>
/// A row waits from the moment it could first be dispatched: its publish time for a delayed row, otherwise when it was
/// stored. Relational storages measure the age on the database clock, so an application clock skewed from the
/// database does not move it.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class BacklogView
{
    /// <summary>Gets the number of published (outbox) rows still waiting to reach the broker.</summary>
    public required long PublishedPending { get; init; }

    /// <summary>Gets the number of received (inbox) rows still waiting to be handled.</summary>
    public required long ReceivedPending { get; init; }

    /// <summary>
    /// Gets how long the oldest pending published row has been waiting, or <see langword="null"/> when none is.
    /// </summary>
    public TimeSpan? OldestPublishedPendingAge { get; init; }

    /// <summary>
    /// Gets how long the oldest pending received row has been waiting, or <see langword="null"/> when none is.
    /// </summary>
    public TimeSpan? OldestReceivedPendingAge { get; init; }
}
