// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;

namespace Headless.Messaging.Persistence;

/// <summary>
/// Optional storage capability that atomically claims delayed published messages for local scheduling.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must select no more than the configured scheduler batch size, transition each
/// winner to <see cref="StatusName.Queued"/>, and stamp an ownership lease from the same authoritative
/// store clock used to test lease expiry. For a future schedule, the lease must extend from the later
/// of that clock or the message expiration time so it remains valid until the first dispatch attempt.
/// The returned messages must reflect the committed durable <c>LockedUntil</c> and <c>Owner</c> values.
/// </para>
/// <para>
/// Providers that do not implement this capability continue through
/// <see cref="IDataStorage.ScheduleMessagesOfDelayedAsync"/>. Callers must not enqueue returned messages
/// until this method completes because completion is the commit boundary.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDelayedMessageClaimStorage
{
    /// <summary>Atomically claims and transitions a bounded batch of delayed messages.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Committed claim winners in deterministic schedule order.</returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the durable claim commits.
    /// </exception>
    ValueTask<IReadOnlyList<MediumMessage>> ClaimDelayedMessagesAsync(CancellationToken cancellationToken = default);
}
