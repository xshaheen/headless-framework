// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>
/// Allocates values from fast-mode counters partitioned by current tenant, counter name, and an optional partition key.
/// </summary>
/// <remarks>
/// This singleton commits increments on dedicated database connections without joining the caller transaction.
/// Numbers are unique across processes, but failed operations after allocation leave gaps.
/// Counters configured as <see cref="SequenceMode.GapFree" /> throw; access them through
/// <c>unit.Sequences</c> on the writing unit of work.
/// </remarks>
[PublicAPI]
public interface ISequenceGenerator
{
    /// <summary>Allocates the next value from the counter.</summary>
    /// <param name="name">The counter name, compared ordinally.</param>
    /// <param name="partition">
    /// An optional partition key, such as a year. A new partition resets the counter to the policy starting value.
    /// Pass <see langword="null" /> or an empty string to use no partition.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The allocated value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name" /> is empty, whitespace, or invalid, or <paramref name="partition" /> or the tenant identifier contains invalid characters or exceeds maximum length.
    /// </exception>
    /// <exception cref="InvalidOperationException">The counter is registered as <see cref="SequenceMode.GapFree"/>.</exception>
    ValueTask<long> NextAsync(string name, string? partition = null, CancellationToken cancellationToken = default);

    /// <summary>Allocates consecutive values from the counter atomically.</summary>
    /// <param name="name">The counter name, compared ordinally.</param>
    /// <param name="count">The number of values to allocate. Must be at least 1.</param>
    /// <param name="partition">
    /// An optional partition key, such as a year. Pass <see langword="null" /> or an empty string to use no partition.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The allocated range of values in ascending order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count" /> is less than 1.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name" /> is empty, whitespace, or invalid, or <paramref name="partition" /> or the tenant identifier contains invalid characters or exceeds maximum length.
    /// </exception>
    /// <exception cref="InvalidOperationException">The counter is registered as <see cref="SequenceMode.GapFree"/>.</exception>
    /// <exception cref="OverflowException">The requested range exceeds <see cref="long.MaxValue" />.</exception>
    ValueTask<SequenceRange> ReserveAsync(
        string name,
        int count,
        string? partition = null,
        CancellationToken cancellationToken = default
    );
}
