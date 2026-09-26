// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;

namespace Headless.Idempotency;

/// <summary>
/// Autonomous durable idempotency: each call runs in its own transaction and commits before it returns, so an
/// admission is visible to every other process at once.
/// </summary>
/// <remarks>
/// <para>
/// An admission is keyed by the current tenant and the caller's idempotency key. Exactly one concurrent admission of
/// a key is <see cref="IdempotentDisposition.Admitted" /> and holds a fenced lease; the rest see
/// <see cref="IdempotentDisposition.InFlight" /> until it completes, then <see cref="IdempotentDisposition.Replay" />.
/// When the admitted attempt's lease expires without completing, the next admission takes the operation over, and the
/// expired attempt's completion is refused.
/// </para>
/// <para>
/// To make the operation's own writes commit only while it still owns the key, run them in a unit of work that calls
/// <c>unit.Idempotency.FenceAsync(admission)</c> first and <c>unit.Idempotency.CompleteAsync</c> last. Use
/// <c>unit.Idempotency.AdmitAsync</c> instead of this interface when the admission itself must roll back with the
/// caller's transaction.
/// </para>
/// </remarks>
[PublicAPI]
public interface IIdempotentOperations
{
    /// <summary>Admits <paramref name="key" /> for the request <paramref name="fingerprint" /> identifies.</summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="fingerprint">The request's fingerprint.</param>
    /// <param name="expectedContract">
    /// The result contract the caller can read; a completed result stored under another contract is a conflict rather
    /// than a replay. <see langword="null" /> accepts any contract.
    /// </param>
    /// <param name="leaseDuration">How long the admitted attempt owns the key unless renewed; the configured default when <see langword="null" />.</param>
    /// <param name="retention">How long the record replays after completion; the configured default when <see langword="null" />.</param>
    /// <param name="cancellationToken">Token used to cancel the database calls.</param>
    /// <returns>The admission.</returns>
    /// <exception cref="ArgumentException">
    /// The key, fingerprint algorithm, expected contract, or current tenant id is invalid.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A duration is outside its bounds.</exception>
    /// <exception cref="NotSupportedException">
    /// The key's record carries a fingerprint from an algorithm this version does not know.
    /// </exception>
    ValueTask<IdempotentAdmission> AdmitAsync(
        string key,
        IdempotencyFingerprint fingerprint,
        string? expectedContract = null,
        TimeSpan? leaseDuration = null,
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores the admitted operation's result and settles its lease in one transaction, so later admissions replay it.
    /// </summary>
    /// <param name="admission">The admitted operation.</param>
    /// <param name="result">The result bytes.</param>
    /// <param name="contract">The contract tag the bytes are written under.</param>
    /// <param name="retention">How long the result replays; the admission's retention when <see langword="null" />.</param>
    /// <param name="cancellationToken">Token used to cancel the database calls before the commit.</param>
    /// <returns>A task that completes when the result is committed.</returns>
    /// <exception cref="ArgumentException">The admission is not admitted, or the contract is invalid.</exception>
    /// <exception cref="StaleLeaseException">
    /// The attempt no longer owns the key (its lease expired or was taken over); nothing was stored.
    /// </exception>
    ValueTask CompleteAsync(
        IdempotentAdmission admission,
        ReadOnlyMemory<byte> result,
        string contract,
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gives the admitted operation up without a result, so the next admission of the key proceeds at once.</summary>
    /// <param name="admission">The admitted operation.</param>
    /// <param name="cancellationToken">Token used to cancel the database calls before the commit.</param>
    /// <returns>
    /// <see cref="LeaseSettlementStatus.Released" /> on success, or why the release was refused; a refusal writes
    /// nothing.
    /// </returns>
    /// <exception cref="ArgumentException">The admission is not admitted.</exception>
    ValueTask<LeaseSettlementStatus> ReleaseAsync(
        IdempotentAdmission admission,
        CancellationToken cancellationToken = default
    );

    /// <summary>Extends the admitted operation's lease to <paramref name="duration" /> from now, by the database clock.</summary>
    /// <param name="admission">The admitted operation.</param>
    /// <param name="duration">The lease's new time to live.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>The renewal's result; anything but renewed means the attempt no longer owns the key.</returns>
    /// <exception cref="ArgumentException">The admission is not admitted.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration" /> is outside the fencing bounds.</exception>
    ValueTask<LeaseRenewalResult> RenewAsync(
        IdempotentAdmission admission,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads <paramref name="key" />'s current status for the current tenant without taking a row lock or touching
    /// its lease. Cheap enough to poll while waiting on another attempt; a caller that needs the live disposition,
    /// the lease expiry, or the stored result still calls <see cref="AdmitAsync" />.
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>
    /// <see cref="IdempotencyPeekStatus.Absent" /> when no record exists or its retention already elapsed;
    /// otherwise <see cref="IdempotencyPeekStatus.Pending" /> or <see cref="IdempotencyPeekStatus.Completed" />.
    /// </returns>
    /// <exception cref="ArgumentException">The key or current tenant id is invalid.</exception>
    ValueTask<IdempotencyPeekStatus> PeekAsync(string key, CancellationToken cancellationToken = default);
}
