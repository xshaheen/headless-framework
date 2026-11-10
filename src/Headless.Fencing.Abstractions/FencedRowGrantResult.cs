// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;

#pragma warning disable CA1000 // The factories name the key type, which no argument carries for inference to find.
namespace Headless.Fencing;

/// <summary>The result of a grant on a row lease: the acquired attempt, or the attempt that kept the row.</summary>
/// <typeparam name="TKey">The row's key type.</typeparam>
[PublicAPI]
public sealed class FencedRowGrantResult<TKey>
    where TKey : notnull
{
    private FencedRowGrantResult(
        LeaseGrantStatus status,
        FencedRow<TKey>? row,
        DateTimeOffset expiresAt,
        long? holderGeneration,
        long? previousGeneration,
        int takeoverCount
    )
    {
        Status = status;
        Row = row;
        ExpiresAt = expiresAt;
        HolderGeneration = holderGeneration;
        PreviousGeneration = previousGeneration;
        TakeoverCount = takeoverCount;
    }

    /// <summary>Gets what the grant did.</summary>
    public LeaseGrantStatus Status { get; }

    /// <summary>
    /// Gets the acquired attempt's token when <see cref="Status" /> is <see cref="LeaseGrantStatus.Granted" /> or
    /// <see cref="LeaseGrantStatus.Takeover" />; otherwise <see langword="null" />.
    /// </summary>
    public FencedRow<TKey>? Row { get; }

    /// <summary>
    /// Gets the acquired attempt's expiry, or the holder's expiry when <see cref="Status" /> is
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
    /// Gets how many times the row's work was taken from an expired attempt since an attempt last settled, was
    /// released, or was revoked, including this grant when it is a <see cref="LeaseGrantStatus.Takeover" />. For
    /// <see cref="LeaseGrantStatus.Held" /> and <see cref="LeaseGrantStatus.Expired" /> it is the holder's count.
    /// </summary>
    /// <remarks>A count that keeps rising means executors keep losing the row before they finish.</remarks>
    public int TakeoverCount { get; }

    /// <summary>Gets whether the caller now holds the row.</summary>
    [MemberNotNullWhen(true, nameof(Row))]
    public bool IsAcquired => Row is not null;

    /// <summary>Creates the result of a grant on a row with no live attempt, or of a re-grant by the same holder.</summary>
    /// <param name="row">The acquired attempt's token.</param>
    /// <param name="expiresAt">The acquired attempt's expiry.</param>
    /// <param name="takeoverCount">The row's takeover count.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="row" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="takeoverCount" /> is negative.</exception>
    public static FencedRowGrantResult<TKey> Granted(
        FencedRow<TKey> row,
        DateTimeOffset expiresAt,
        int takeoverCount = 0
    )
    {
        Argument.IsNotNull(row);
        Argument.IsPositiveOrZero(takeoverCount);

        return new(
            LeaseGrantStatus.Granted,
            row,
            expiresAt,
            holderGeneration: null,
            previousGeneration: null,
            takeoverCount
        );
    }

    /// <summary>Creates the result of a grant that took the row over from an expired attempt.</summary>
    /// <param name="row">The acquired attempt's token.</param>
    /// <param name="expiresAt">The acquired attempt's expiry.</param>
    /// <param name="previousGeneration">The expired attempt's generation.</param>
    /// <param name="takeoverCount">The row's takeover count, including this takeover.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="row" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="previousGeneration" /> is not positive or not below <paramref name="row" />'s generation, or
    /// <paramref name="takeoverCount" /> is not positive.
    /// </exception>
    public static FencedRowGrantResult<TKey> Takeover(
        FencedRow<TKey> row,
        DateTimeOffset expiresAt,
        long previousGeneration,
        int takeoverCount = 1
    )
    {
        Argument.IsNotNull(row);
        Argument.IsPositive(previousGeneration);
        Argument.IsLessThan(previousGeneration, row.Generation);
        Argument.IsPositive(takeoverCount);

        return new(
            LeaseGrantStatus.Takeover,
            row,
            expiresAt,
            holderGeneration: null,
            previousGeneration,
            takeoverCount
        );
    }

    /// <summary>Creates the result of a grant refused because a live attempt holds the row.</summary>
    /// <param name="holderGeneration">The live attempt's generation.</param>
    /// <param name="holderExpiresAt">The live attempt's expiry.</param>
    /// <param name="takeoverCount">The live attempt's takeover count.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="holderGeneration" /> is not positive, or <paramref name="takeoverCount" /> is negative.
    /// </exception>
    public static FencedRowGrantResult<TKey> Held(
        long holderGeneration,
        DateTimeOffset holderExpiresAt,
        int takeoverCount = 0
    )
    {
        Argument.IsPositive(holderGeneration);
        Argument.IsPositiveOrZero(takeoverCount);

        return new(
            LeaseGrantStatus.Held,
            row: null,
            holderExpiresAt,
            holderGeneration,
            previousGeneration: null,
            takeoverCount
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
    public static FencedRowGrantResult<TKey> Expired(
        long holderGeneration,
        DateTimeOffset holderExpiresAt,
        int takeoverCount = 0
    )
    {
        Argument.IsPositive(holderGeneration);
        Argument.IsPositiveOrZero(takeoverCount);

        return new(
            LeaseGrantStatus.Expired,
            row: null,
            holderExpiresAt,
            holderGeneration,
            previousGeneration: null,
            takeoverCount
        );
    }
}
