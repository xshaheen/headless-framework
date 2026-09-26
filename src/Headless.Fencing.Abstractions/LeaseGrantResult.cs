// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;

namespace Headless.Fencing;

/// <summary>The result of a grant: the acquired lease, or the live holder that kept it.</summary>
[PublicAPI]
public sealed class LeaseGrantResult
{
    private LeaseGrantResult(
        LeaseGrantStatus status,
        FencedLease? lease,
        DateTimeOffset expiresAt,
        long? holderGeneration,
        long? previousGeneration
    )
    {
        Status = status;
        Lease = lease;
        ExpiresAt = expiresAt;
        HolderGeneration = holderGeneration;
        PreviousGeneration = previousGeneration;
    }

    /// <summary>Gets what the grant did.</summary>
    public LeaseGrantStatus Status { get; }

    /// <summary>
    /// Gets the acquired lease when <see cref="Status" /> is <see cref="LeaseGrantStatus.Granted" /> or
    /// <see cref="LeaseGrantStatus.Takeover" />; otherwise <see langword="null" />.
    /// </summary>
    public FencedLease? Lease { get; }

    /// <summary>
    /// Gets the acquired lease's expiry, or the live holder's expiry when <see cref="Status" /> is
    /// <see cref="LeaseGrantStatus.Held" />. Decided by the database clock.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Gets the live holder's generation when <see cref="Status" /> is <see cref="LeaseGrantStatus.Held" />;
    /// otherwise <see langword="null" />.
    /// </summary>
    public long? HolderGeneration { get; }

    /// <summary>
    /// Gets the generation that was taken over when <see cref="Status" /> is <see cref="LeaseGrantStatus.Takeover" />;
    /// otherwise <see langword="null" />.
    /// </summary>
    public long? PreviousGeneration { get; }

    /// <summary>Gets whether the caller now holds the lease.</summary>
    [MemberNotNullWhen(true, nameof(Lease))]
    public bool IsAcquired => Lease is not null;

    /// <summary>Creates the result of a grant on a lease with no live holder.</summary>
    /// <param name="lease">The acquired lease.</param>
    /// <param name="expiresAt">The acquired lease's expiry.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lease" /> is <see langword="null" />.</exception>
    public static LeaseGrantResult Granted(FencedLease lease, DateTimeOffset expiresAt)
    {
        Argument.IsNotNull(lease);

        return new(LeaseGrantStatus.Granted, lease, expiresAt, holderGeneration: null, previousGeneration: null);
    }

    /// <summary>Creates the result of a grant that took over an expired attempt's lease.</summary>
    /// <param name="lease">The acquired lease.</param>
    /// <param name="expiresAt">The acquired lease's expiry.</param>
    /// <param name="previousGeneration">The expired attempt's generation.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lease" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="previousGeneration" /> is not positive or not below <paramref name="lease" />'s generation.
    /// </exception>
    public static LeaseGrantResult Takeover(FencedLease lease, DateTimeOffset expiresAt, long previousGeneration)
    {
        Argument.IsNotNull(lease);
        Argument.IsPositive(previousGeneration);
        Argument.IsLessThan(previousGeneration, lease.Generation);

        return new(LeaseGrantStatus.Takeover, lease, expiresAt, holderGeneration: null, previousGeneration);
    }

    /// <summary>Creates the result of a grant refused because a live attempt holds the lease.</summary>
    /// <param name="holderGeneration">The live holder's generation.</param>
    /// <param name="holderExpiresAt">The live holder's expiry.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="holderGeneration" /> is not positive.</exception>
    public static LeaseGrantResult Held(long holderGeneration, DateTimeOffset holderExpiresAt)
    {
        Argument.IsPositive(holderGeneration);

        return new(LeaseGrantStatus.Held, lease: null, holderExpiresAt, holderGeneration, previousGeneration: null);
    }
}
