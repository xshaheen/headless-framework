// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;

namespace Headless.Idempotency;

/// <summary>
/// The enlisted idempotency surface behind <c>unit.Idempotency</c>: every call runs inside the unit's own transaction,
/// so the unit's commit makes it durable and its rollback undoes it.
/// </summary>
/// <remarks>
/// A singleton registered by <c>AddHeadlessIdempotency</c>. It holds no unit: the caller's handle arrives per call,
/// and the call refuses, before any command runs, a unit that is no longer active, carries no relational resource,
/// carries a completed transaction, or carries a transaction the configured provider cannot write through. The record row
/// carries its own lease, so every call locks exactly one row. Enlisted calls are never retried. Application code reaches it through
/// <c>unit.Idempotency</c> rather than directly.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUnitOfWorkIdempotency : IUnitOfWorkFeature
{
    /// <summary>Admits <paramref name="key" /> inside <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction holds the admission.</param>
    /// <param name="key">The idempotency key.</param>
    /// <param name="fingerprint">The request's fingerprint.</param>
    /// <param name="expectedContract">The result contract the caller can read; <see langword="null" /> accepts any.</param>
    /// <param name="leaseDuration">The admitted attempt's lease duration; the configured default when <see langword="null" />.</param>
    /// <param name="retention">How long the record replays after completion; the configured default when <see langword="null" />.</param>
    /// <param name="cancellationToken">Token used to cancel the database commands.</param>
    /// <returns>The admission.</returns>
    /// <exception cref="ArgumentException">
    /// The key, fingerprint algorithm, expected contract, or current tenant id is invalid.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A duration is outside its bounds.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="NotSupportedException">
    /// The key's record carries a fingerprint from an algorithm this version does not know.
    /// </exception>
    ValueTask<IdempotentAdmission> AdmitAsync(
        IUnitOfWork unitOfWork,
        string key,
        IdempotencyFingerprint fingerprint,
        string? expectedContract = null,
        TimeSpan? leaseDuration = null,
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores the admitted operation's result and ends its lease inside <paramref name="unitOfWork" />'s
    /// transaction.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the completion.</param>
    /// <param name="admission">The admitted operation.</param>
    /// <param name="result">The result bytes.</param>
    /// <param name="contract">The contract tag the bytes are written under.</param>
    /// <param name="retention">How long the result replays; the admission's retention when <see langword="null" />.</param>
    /// <param name="cancellationToken">Token used to cancel the database commands.</param>
    /// <returns>A task that completes when the result is written.</returns>
    /// <exception cref="ArgumentException">The admission is not admitted, or the contract is invalid.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="StaleAdmissionException">The attempt no longer owns the key; nothing was written.</exception>
    ValueTask CompleteAsync(
        IUnitOfWork unitOfWork,
        IdempotentAdmission admission,
        ReadOnlyMemory<byte> result,
        string contract,
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Releases the admitted operation without a result inside <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction holds the release.</param>
    /// <param name="admission">The admitted operation.</param>
    /// <param name="cancellationToken">Token used to cancel the database commands.</param>
    /// <returns><see cref="IdempotentLeaseStatus.Released" /> on success, or why the release was refused.</returns>
    /// <exception cref="ArgumentException">The admission is not admitted.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    ValueTask<IdempotentLeaseStatus> ReleaseAsync(
        IUnitOfWork unitOfWork,
        IdempotentAdmission admission,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks inside <paramref name="unitOfWork" />'s transaction that the admitted attempt still owns its key, and
    /// keeps that true until the transaction ends.
    /// </summary>
    /// <param name="unitOfWork">The unit whose writes the fence guards.</param>
    /// <param name="admission">The admitted operation.</param>
    /// <param name="cancellationToken">Token used to cancel the database commands.</param>
    /// <returns>A task that completes when the fence holds.</returns>
    /// <exception cref="ArgumentException">The admission is not admitted.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the read.</exception>
    /// <exception cref="StaleAdmissionException">The attempt no longer owns the key.</exception>
    ValueTask FenceAsync(
        IUnitOfWork unitOfWork,
        IdempotentAdmission admission,
        CancellationToken cancellationToken = default
    );
}
