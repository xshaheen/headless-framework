// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;

namespace Headless.Fencing;

/// <summary>The result of a grant: the acquired lease, or the attempt that kept it.</summary>
[PublicAPI]
public sealed class LeaseGrantResult
{
    private LeaseGrantResult(
        LeaseGrantStatus status,
        FencedLease? lease,
        DateTimeOffset expiresAt,
        long? holderGeneration,
        long? previousGeneration,
        int takeoverCount,
        LeaseProgress? progress
    )
    {
        Status = status;
        Lease = lease;
        ExpiresAt = expiresAt;
        HolderGeneration = holderGeneration;
        PreviousGeneration = previousGeneration;
        TakeoverCount = takeoverCount;
        Progress = progress;
    }

    /// <summary>Gets what the grant did.</summary>
    public LeaseGrantStatus Status { get; }

    /// <summary>
    /// Gets the acquired lease when <see cref="Status" /> is <see cref="LeaseGrantStatus.Granted" /> or
    /// <see cref="LeaseGrantStatus.Takeover" />; otherwise <see langword="null" />.
    /// </summary>
    public FencedLease? Lease { get; }

    /// <summary>
    /// Gets the acquired lease's expiry, or the holder's expiry when <see cref="Status" /> is
    /// <see cref="LeaseGrantStatus.Held" /> or <see cref="LeaseGrantStatus.Expired" /> (already past for an expired
    /// holder). Decided by the database clock.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Gets the holder's generation when <see cref="Status" /> is <see cref="LeaseGrantStatus.Held" /> or
    /// <see cref="LeaseGrantStatus.Expired" />; otherwise <see langword="null" />.
    /// </summary>
    public long? HolderGeneration { get; }

    /// <summary>
    /// Gets the generation that was taken over when <see cref="Status" /> is <see cref="LeaseGrantStatus.Takeover" />;
    /// otherwise <see langword="null" />.
    /// </summary>
    public long? PreviousGeneration { get; }

    /// <summary>
    /// Gets how many times the lease's work was taken from an expired holder since it last settled or was released:
    /// each <see cref="LeaseGrantStatus.Takeover" /> grant and each sweep that abandoned an expired attempt adds one.
    /// For <see cref="LeaseGrantStatus.Held" /> and <see cref="LeaseGrantStatus.Expired" /> it is the holder's count.
    /// </summary>
    /// <remarks>
    /// A count that keeps rising means executors keep losing the lease before they finish: a crash loop, a renewal
    /// cadence too slow for the lease duration, or two workers contending for one resource.
    /// </remarks>
    public int TakeoverCount { get; }

    /// <summary>
    /// Gets the last progress an expired attempt recorded when this grant resumes its work: a
    /// <see cref="LeaseGrantStatus.Takeover" />, or a <see cref="LeaseGrantStatus.Granted" /> over an attempt a sweep
    /// abandoned. <see langword="null" /> when no attempt recorded progress, after a settlement or release, and for
    /// <see cref="LeaseGrantStatus.Held" /> and <see cref="LeaseGrantStatus.Expired" />.
    /// </summary>
    public LeaseProgress? Progress { get; }

    /// <summary>Gets whether the caller now holds the lease.</summary>
    [MemberNotNullWhen(true, nameof(Lease))]
    public bool IsAcquired => Lease is not null;

    /// <summary>Creates the result of a grant on a lease with no live holder.</summary>
    /// <param name="lease">The acquired lease.</param>
    /// <param name="expiresAt">The acquired lease's expiry.</param>
    /// <param name="takeoverCount">
    /// The lease's takeover count: zero for a new or settled lease, and the count a sweep left for an abandoned one.
    /// </param>
    /// <param name="progress">The abandoned attempt's last progress, if the grant resumes one.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lease" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="takeoverCount" /> is negative.</exception>
    public static LeaseGrantResult Granted(
        FencedLease lease,
        DateTimeOffset expiresAt,
        int takeoverCount = 0,
        LeaseProgress? progress = null
    )
    {
        Argument.IsNotNull(lease);
        Argument.IsPositiveOrZero(takeoverCount);

        return new(
            LeaseGrantStatus.Granted,
            lease,
            expiresAt,
            holderGeneration: null,
            previousGeneration: null,
            takeoverCount,
            progress
        );
    }

    /// <summary>Creates the result of a grant that took over an expired attempt's lease.</summary>
    /// <param name="lease">The acquired lease.</param>
    /// <param name="expiresAt">The acquired lease's expiry.</param>
    /// <param name="previousGeneration">The expired attempt's generation.</param>
    /// <param name="takeoverCount">The lease's takeover count, including this takeover.</param>
    /// <param name="progress">The last progress any earlier attempt recorded, if one did.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lease" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="previousGeneration" /> is not positive or not below <paramref name="lease" />'s generation, or
    /// <paramref name="takeoverCount" /> is not positive.
    /// </exception>
    public static LeaseGrantResult Takeover(
        FencedLease lease,
        DateTimeOffset expiresAt,
        long previousGeneration,
        int takeoverCount = 1,
        LeaseProgress? progress = null
    )
    {
        Argument.IsNotNull(lease);
        Argument.IsPositive(previousGeneration);
        Argument.IsLessThan(previousGeneration, lease.Generation);
        Argument.IsPositive(takeoverCount);

        return new(
            LeaseGrantStatus.Takeover,
            lease,
            expiresAt,
            holderGeneration: null,
            previousGeneration,
            takeoverCount,
            progress
        );
    }

    /// <summary>Creates the result of a grant refused because a live attempt holds the lease.</summary>
    /// <param name="holderGeneration">The live holder's generation.</param>
    /// <param name="holderExpiresAt">The live holder's expiry.</param>
    /// <param name="takeoverCount">The live holder's takeover count.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="holderGeneration" /> is not positive, or <paramref name="takeoverCount" /> is negative.
    /// </exception>
    public static LeaseGrantResult Held(long holderGeneration, DateTimeOffset holderExpiresAt, int takeoverCount = 0)
    {
        Argument.IsPositive(holderGeneration);
        Argument.IsPositiveOrZero(takeoverCount);

        return new(
            LeaseGrantStatus.Held,
            lease: null,
            holderExpiresAt,
            holderGeneration,
            previousGeneration: null,
            takeoverCount,
            progress: null
        );
    }

    /// <summary>
    /// Creates the result of a grant that asked for <see cref="LeaseTakeover.AfterSweep" /> and found the last attempt
    /// expired but not yet abandoned, settled, or released.
    /// </summary>
    /// <param name="holderGeneration">The expired attempt's generation.</param>
    /// <param name="holderExpiresAt">When the expired attempt's lease ran out.</param>
    /// <param name="takeoverCount">The expired attempt's takeover count.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="holderGeneration" /> is not positive, or <paramref name="takeoverCount" /> is negative.
    /// </exception>
    public static LeaseGrantResult Expired(long holderGeneration, DateTimeOffset holderExpiresAt, int takeoverCount = 0)
    {
        Argument.IsPositive(holderGeneration);
        Argument.IsPositiveOrZero(takeoverCount);

        return new(
            LeaseGrantStatus.Expired,
            lease: null,
            holderExpiresAt,
            holderGeneration,
            previousGeneration: null,
            takeoverCount,
            progress: null
        );
    }
}
