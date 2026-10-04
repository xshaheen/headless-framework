// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>
/// Issues numbers from fast-mode counters keyed by the current tenant, the counter name, and an optional partition.
/// </summary>
/// <remarks>
/// A singleton that never joins the caller's transaction: every call commits its increment on its own connection
/// before it returns. A number is therefore never issued twice, even across processes, but a caller that fails
/// after taking one leaves a gap. Counters registered as <see cref="SequenceMode.GapFree" /> are refused here;
/// take them through <c>unit.Sequences</c> on the unit of work that writes the number.
/// </remarks>
[PublicAPI]
public interface ISequenceGenerator
{
    /// <summary>Takes the counter's next value.</summary>
    /// <param name="name">The counter name, compared ordinally.</param>
    /// <param name="partition">
    /// Splits the counter, for example by year. A new partition starts a new counter at the policy's start
    /// value. <see langword="null" /> or empty means no partition.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The value taken.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name" /> is empty, whitespace, or invalid, or <paramref name="partition" /> or the tenant identifier contains invalid characters or exceeds maximum length.
    /// </exception>
    /// <exception cref="InvalidOperationException">The counter is registered as <see cref="SequenceMode.GapFree"/>.</exception>
    ValueTask<long> NextAsync(string name, string? partition = null, CancellationToken cancellationToken = default);

    /// <summary>Atomically takes <paramref name="count" /> consecutive values from the counter.</summary>
    /// <param name="name">The counter name, compared ordinally.</param>
    /// <param name="count">How many values to take; at least 1.</param>
    /// <param name="partition">
    /// Splits the counter, for example by year. <see langword="null" /> or empty means no partition.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The values taken, in order.</returns>
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
