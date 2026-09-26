// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Idempotency;

/// <summary>
/// One unit-of-work handle bound to durable idempotency, returned by <c>unit.Idempotency</c>. Every call runs inside
/// that unit's transaction, so it takes effect only when the unit commits.
/// </summary>
/// <remarks>
/// One binding per unit, created on the first read of <c>unit.Idempotency</c> and kept as unit-local state; it owns
/// nothing to dispose. The unit's liveness is checked on each call, so a binding kept past the unit's completion
/// throws on its next call.
/// </remarks>
[PublicAPI]
public sealed class UnitOfWorkIdempotency
{
    private readonly IUnitOfWorkIdempotency _idempotency;
    private readonly IUnitOfWork _unitOfWork;

    internal UnitOfWorkIdempotency(IUnitOfWorkIdempotency idempotency, IUnitOfWork unitOfWork)
    {
        _idempotency = idempotency;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Admits <paramref name="key" /> inside the bound unit's transaction.</summary>
    /// <remarks>
    /// The key's record stays locked until the unit ends: a concurrent admission of the key waits, then sees the
    /// outcome of this unit. An admission the unit rolls back leaves no record.
    /// </remarks>
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
    /// <exception cref="InvalidOperationException">The bound unit is no longer active or cannot host the write.</exception>
    /// <exception cref="NotSupportedException">
    /// The key's record carries a fingerprint from an algorithm this version does not know.
    /// </exception>
    public ValueTask<IdempotentAdmission> AdmitAsync(
        string key,
        IdempotencyFingerprint fingerprint,
        string? expectedContract = null,
        TimeSpan? leaseDuration = null,
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default
    )
    {
        return _idempotency.AdmitAsync(
            _unitOfWork,
            key,
            fingerprint,
            expectedContract,
            leaseDuration,
            retention,
            cancellationToken
        );
    }

    /// <summary>
    /// Stores the admitted operation's result and ends its lease inside the bound unit's transaction, so the
    /// result commits together with the operation's own writes.
    /// </summary>
    /// <param name="admission">The admitted operation.</param>
    /// <param name="result">The result bytes.</param>
    /// <param name="contract">The contract tag the bytes are written under.</param>
    /// <param name="retention">How long the result replays; the admission's retention when <see langword="null" />.</param>
    /// <param name="cancellationToken">Token used to cancel the database commands.</param>
    /// <returns>A task that completes when the result is written.</returns>
    /// <exception cref="ArgumentException">The admission is not admitted, or the contract is invalid.</exception>
    /// <exception cref="InvalidOperationException">The bound unit is no longer active or cannot host the write.</exception>
    /// <exception cref="StaleAdmissionException">
    /// The attempt no longer owns the key; nothing was written. Let it roll the unit back.
    /// </exception>
    public ValueTask CompleteAsync(
        IdempotentAdmission admission,
        ReadOnlyMemory<byte> result,
        string contract,
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default
    )
    {
        return _idempotency.CompleteAsync(_unitOfWork, admission, result, contract, retention, cancellationToken);
    }

    /// <summary>Releases the admitted operation without a result inside the bound unit's transaction.</summary>
    /// <param name="admission">The admitted operation.</param>
    /// <param name="cancellationToken">Token used to cancel the database commands.</param>
    /// <returns><see cref="IdempotentLeaseStatus.Released" /> on success, or why the release was refused.</returns>
    /// <exception cref="ArgumentException">The admission is not admitted.</exception>
    /// <exception cref="InvalidOperationException">The bound unit is no longer active or cannot host the write.</exception>
    public ValueTask<IdempotentLeaseStatus> ReleaseAsync(
        IdempotentAdmission admission,
        CancellationToken cancellationToken = default
    )
    {
        return _idempotency.ReleaseAsync(_unitOfWork, admission, cancellationToken);
    }

    /// <summary>
    /// Refuses the bound unit's writes unless the admitted attempt still owns its key, and keeps that answer true
    /// until the unit commits.
    /// </summary>
    /// <remarks>
    /// Call it before the writes it guards. It takes the key's record row with an update-intent lock held until the
    /// unit ends, so a concurrent admission, renewal, or completion of the key waits for this unit's outcome instead
    /// of deciding around it; the record is the only row any idempotency call locks, so there is no lock order to get
    /// wrong. Safe to repeat, and never makes the unit non-retryable.
    /// </remarks>
    /// <param name="admission">The admitted operation.</param>
    /// <param name="cancellationToken">Token used to cancel the database commands.</param>
    /// <returns>A task that completes when the fence holds.</returns>
    /// <exception cref="ArgumentException">The admission is not admitted.</exception>
    /// <exception cref="InvalidOperationException">The bound unit is no longer active or cannot host the read.</exception>
    /// <exception cref="StaleAdmissionException">The attempt no longer owns the key.</exception>
    public ValueTask FenceAsync(IdempotentAdmission admission, CancellationToken cancellationToken = default)
    {
        return _idempotency.FenceAsync(_unitOfWork, admission, cancellationToken);
    }
}
