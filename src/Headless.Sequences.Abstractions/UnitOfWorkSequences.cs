// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Sequences;

/// <summary>
/// Provides one unit-of-work handle bound to gap-free numbering, returned by <c>unit.Sequences</c>. A number
/// taken through it is written inside that unit's transaction, so it is kept only when the unit commits.
/// </summary>
/// <remarks>
/// One binding per unit, created on the first read of <c>unit.Sequences</c> and kept as unit-local state;
/// it owns nothing to dispose. The unit's liveness is checked on each call, so a binding kept past the
/// unit's completion throws on its next call.
/// </remarks>
[PublicAPI]
public sealed class UnitOfWorkSequences
{
    private readonly IUnitOfWorkSequences _sequences;
    private readonly IUnitOfWork _unitOfWork;

    internal UnitOfWorkSequences(IUnitOfWorkSequences sequences, IUnitOfWork unitOfWork)
    {
        _sequences = sequences;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Takes the gap-free counter's next value inside the bound unit's transaction.</summary>
    /// <remarks>
    /// The counter's row stays locked until the unit commits or rolls back, and every other writer of the
    /// counter waits for it. Take the number as late in the unit as possible, and take several counters in a
    /// fixed order.
    /// </remarks>
    /// <param name="name">The counter name. Must be registered as <see cref="SequenceMode.GapFree" />.</param>
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
    /// <exception cref="InvalidOperationException">
    /// The counter is not registered as gap-free, or the bound unit is no longer active or cannot host the write.
    /// </exception>
    public ValueTask<long> NextAsync(
        string name,
        string? partition = null,
        CancellationToken cancellationToken = default
    )
    {
        return _sequences.NextAsync(_unitOfWork, name, partition, cancellationToken);
    }

    /// <summary>
    /// Takes the gap-free counter's next value as a document number inside the bound unit's transaction: in the
    /// partition its policy's reset chooses for today (such as the year), and formatted with its policy's template
    /// (such as <c>REC-{yyyy}-{seq:D6}</c>).
    /// </summary>
    /// <remarks>
    /// The counter's row stays locked until the unit commits or rolls back, and a rollback returns the number, so the
    /// series has no gaps. A retried request that replays its stored response through idempotency gets the same
    /// number back; one whose first attempt rolled back takes the number again.
    /// </remarks>
    /// <param name="name">The counter name. Must be registered as <see cref="SequenceMode.GapFree" />.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The document number.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name" /> or the tenant identifier is invalid.</exception>
    /// <exception cref="InvalidOperationException">
    /// The counter is not registered as gap-free, or the bound unit is no longer active or cannot host the write.
    /// </exception>
    public ValueTask<SequenceNumber> NextNumberAsync(string name, CancellationToken cancellationToken = default)
    {
        return _sequences.NextNumberAsync(_unitOfWork, name, cancellationToken);
    }

    /// <summary>
    /// Moves a reported counter forward to <paramref name="value" /> inside the bound unit's transaction: the
    /// replay guard for numbers a device stamps on its own requests, such as a payment terminal's transaction counter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counter stores the highest value accepted. A value at or below it is <see cref="SequenceAdvanceStatus.Stale" />
    /// and changes nothing: refuse the request, because the same physical transaction may already be recorded. A value
    /// past the next expected one is <see cref="SequenceAdvanceStatus.Skipped" /> and is accepted; alert on it, or throw
    /// to roll the unit back where a gap must be refused.
    /// </para>
    /// <para>
    /// Call it in the unit that records the reported operation, so a rollback leaves the counter where it was and the
    /// device can send the same value again. The counter's row stays locked until the unit ends, so concurrent reports
    /// from one device are handled one at a time.
    /// </para>
    /// </remarks>
    /// <param name="name">The counter name. Must be registered as <see cref="SequenceMode.Reported" />.</param>
    /// <param name="value">The value the device reported.</param>
    /// <param name="partition">The device or source the value belongs to, such as a terminal id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Whether the value was the next one, skipped ahead, or stale, and the highest value accepted before.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name" />, <paramref name="partition" />, or the tenant identifier is invalid.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The counter is not registered as reported, or the bound unit is no longer active or cannot host the write.
    /// </exception>
    public ValueTask<SequenceAdvance> AdvanceToAsync(
        string name,
        long value,
        string? partition = null,
        CancellationToken cancellationToken = default
    )
    {
        return _sequences.AdvanceToAsync(_unitOfWork, name, value, partition, cancellationToken);
    }
}
