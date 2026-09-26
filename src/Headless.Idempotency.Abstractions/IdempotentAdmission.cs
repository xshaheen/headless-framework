// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Fencing;

namespace Headless.Idempotency;

/// <summary>
/// The outcome of admitting an idempotency key: the caller owns the operation, another attempt does, the stored result
/// replays, or the key conflicts.
/// </summary>
/// <remarks>
/// An admitted operation holds a fenced lease of kind <see cref="LeaseKind" /> on its key. Fence the operation's own
/// writes with <c>unit.Idempotency.FenceAsync(admission)</c>, never with <c>unit.Leases.FenceAsync</c> on the lease
/// directly: every idempotency call locks the record before the lease, and locking them in the other order deadlocks
/// against a concurrent admission of the same key.
/// </remarks>
[PublicAPI]
public sealed class IdempotentAdmission
{
    /// <summary>The fenced-lease kind an admitted operation holds; the lease resource is the idempotency key.</summary>
    public const string LeaseKind = "headless.idempotency";

    private IdempotentAdmission(
        IdempotentDisposition disposition,
        IdempotencyKey key,
        IdempotencyFingerprint fingerprint,
        FencedLease? lease = null,
        DateTimeOffset? leaseExpiresAt = null,
        bool isTakeover = false,
        TimeSpan? retention = null,
        IdempotentResult? result = null,
        IdempotencyFingerprint? storedFingerprint = null,
        string? storedContract = null
    )
    {
        Disposition = disposition;
        Key = key;
        Fingerprint = fingerprint;
        Lease = lease;
        LeaseExpiresAt = leaseExpiresAt;
        IsTakeover = isTakeover;
        Retention = retention;
        Result = result;
        StoredFingerprint = storedFingerprint;
        StoredContract = storedContract;
    }

    /// <summary>Gets what the admission decided.</summary>
    public IdempotentDisposition Disposition { get; }

    /// <summary>Gets the operation's tenant and key.</summary>
    public IdempotencyKey Key { get; }

    /// <summary>Gets the fingerprint the caller admitted with.</summary>
    public IdempotencyFingerprint Fingerprint { get; }

    /// <summary>
    /// Gets the fenced lease the caller holds when <see cref="Disposition" /> is
    /// <see cref="IdempotentDisposition.Admitted" />; otherwise <see langword="null" />.
    /// </summary>
    public FencedLease? Lease { get; }

    /// <summary>
    /// Gets the caller's lease expiry when admitted, or the live holder's expiry when
    /// <see cref="IdempotentDisposition.InFlight" />; otherwise <see langword="null" />. Decided by the database clock.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAt { get; }

    /// <summary>
    /// Gets whether an earlier attempt was admitted for this key and ended without completing or releasing (it
    /// crashed, stalled past its lease, or was abandoned by a sweep). Its partial side effects may exist, so an
    /// operation that is not naturally idempotent should check before redoing them.
    /// </summary>
    public bool IsTakeover { get; }

    /// <summary>
    /// Gets how long the record is kept after completion or release when the caller does not say otherwise; set only
    /// when admitted.
    /// </summary>
    public TimeSpan? Retention { get; }

    /// <summary>
    /// Gets the stored result when <see cref="Disposition" /> is <see cref="IdempotentDisposition.Replay" />;
    /// otherwise <see langword="null" />.
    /// </summary>
    public IdempotentResult? Result { get; }

    /// <summary>
    /// Gets the fingerprint the key is stored with when <see cref="Disposition" /> is
    /// <see cref="IdempotentDisposition.Conflict" />; otherwise <see langword="null" />.
    /// </summary>
    public IdempotencyFingerprint? StoredFingerprint { get; }

    /// <summary>
    /// Gets the stored result's contract when the conflict is a contract mismatch rather than a fingerprint mismatch;
    /// otherwise <see langword="null" />.
    /// </summary>
    public string? StoredContract { get; }

    /// <summary>Gets whether the caller owns the operation and holds <see cref="Lease" />.</summary>
    [MemberNotNullWhen(true, nameof(Lease), nameof(Retention))]
    public bool IsAdmitted => Disposition == IdempotentDisposition.Admitted;

    /// <summary>Creates an admission that owns the operation.</summary>
    /// <param name="key">The operation's tenant and key.</param>
    /// <param name="fingerprint">The admitted request's fingerprint.</param>
    /// <param name="lease">The fenced lease the caller holds.</param>
    /// <param name="leaseExpiresAt">The lease's expiry.</param>
    /// <param name="isTakeover">Whether an earlier admitted attempt ended without completing or releasing.</param>
    /// <param name="retention">How long the record is kept after completion or release by default.</param>
    /// <returns>The admission.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="fingerprint" /> or <paramref name="lease" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="lease" /> is not an idempotency lease for <paramref name="key" />.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retention" /> is not positive.</exception>
    public static IdempotentAdmission Admitted(
        IdempotencyKey key,
        IdempotencyFingerprint fingerprint,
        FencedLease lease,
        DateTimeOffset leaseExpiresAt,
        bool isTakeover,
        TimeSpan retention
    )
    {
        Argument.IsNotNull(fingerprint);
        Argument.IsNotNull(lease);
        Argument.IsPositive(retention);

        // The lease must be the one this key's record is fenced by; a lease for another identity would let the
        // operation's fence and completion check a lease no concurrent admission of this key ever touches.
        if (
            !string.Equals(lease.Kind, LeaseKind, StringComparison.Ordinal)
            || !string.Equals(lease.Resource, key.Key, StringComparison.Ordinal)
            || !string.Equals(lease.TenantId, key.TenantId, StringComparison.Ordinal)
        )
        {
            throw new ArgumentException(
                $"An admitted operation's lease must be of kind '{LeaseKind}' with the idempotency key as its "
                    + "resource and the same tenant.",
                nameof(lease)
            );
        }

        return new(IdempotentDisposition.Admitted, key, fingerprint, lease, leaseExpiresAt, isTakeover, retention);
    }

    /// <summary>Creates an admission refused because a live attempt owns the operation.</summary>
    /// <param name="key">The operation's tenant and key.</param>
    /// <param name="fingerprint">The caller's fingerprint.</param>
    /// <param name="holderExpiresAt">The live attempt's lease expiry.</param>
    /// <returns>The admission.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fingerprint" /> is <see langword="null" />.</exception>
    public static IdempotentAdmission InFlight(
        IdempotencyKey key,
        IdempotencyFingerprint fingerprint,
        DateTimeOffset holderExpiresAt
    )
    {
        Argument.IsNotNull(fingerprint);

        return new(IdempotentDisposition.InFlight, key, fingerprint, leaseExpiresAt: holderExpiresAt);
    }

    /// <summary>Creates an admission that replays a completed operation's stored result.</summary>
    /// <param name="key">The operation's tenant and key.</param>
    /// <param name="fingerprint">The caller's fingerprint.</param>
    /// <param name="result">The stored result.</param>
    /// <returns>The admission.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="fingerprint" /> or <paramref name="result" /> is <see langword="null" />.
    /// </exception>
    public static IdempotentAdmission Replay(
        IdempotencyKey key,
        IdempotencyFingerprint fingerprint,
        IdempotentResult result
    )
    {
        Argument.IsNotNull(fingerprint);
        Argument.IsNotNull(result);

        return new(IdempotentDisposition.Replay, key, fingerprint, result: result);
    }

    /// <summary>Creates an admission refused because the key is stored with a different fingerprint.</summary>
    /// <param name="key">The operation's tenant and key.</param>
    /// <param name="fingerprint">The caller's fingerprint.</param>
    /// <param name="storedFingerprint">The fingerprint the key is stored with.</param>
    /// <returns>The admission.</returns>
    /// <exception cref="ArgumentNullException">A fingerprint is <see langword="null" />.</exception>
    public static IdempotentAdmission FingerprintConflict(
        IdempotencyKey key,
        IdempotencyFingerprint fingerprint,
        IdempotencyFingerprint storedFingerprint
    )
    {
        Argument.IsNotNull(fingerprint);
        Argument.IsNotNull(storedFingerprint);

        return new(IdempotentDisposition.Conflict, key, fingerprint, storedFingerprint: storedFingerprint);
    }

    /// <summary>
    /// Creates an admission refused because the completed result was stored under a different contract than the
    /// caller expects.
    /// </summary>
    /// <param name="key">The operation's tenant and key.</param>
    /// <param name="fingerprint">The caller's fingerprint, which matches the stored one.</param>
    /// <param name="storedContract">The stored result's contract.</param>
    /// <returns>The admission.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fingerprint" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="storedContract" /> is empty or whitespace.</exception>
    public static IdempotentAdmission ContractConflict(
        IdempotencyKey key,
        IdempotencyFingerprint fingerprint,
        string storedContract
    )
    {
        Argument.IsNotNull(fingerprint);
        Argument.IsNotNullOrWhiteSpace(storedContract);

        return new(
            IdempotentDisposition.Conflict,
            key,
            fingerprint,
            storedFingerprint: fingerprint,
            storedContract: storedContract
        );
    }
}
