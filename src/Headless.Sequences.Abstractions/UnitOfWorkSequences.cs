// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Sequences;

/// <summary>
/// One unit-of-work handle bound to gap-free numbering, returned by <c>unit.Sequences</c>. A number taken through
/// it is written inside that unit's transaction, so it is kept only when the unit commits.
/// </summary>
/// <remarks>
/// One binding per unit, created on the first read of <c>unit.Sequences</c> and kept as unit-local state; it owns
/// nothing to dispose. The unit's liveness is checked on each call, so a binding kept past the unit's completion
/// throws on its next call.
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
    /// The counter's row stays locked until the unit commits or rolls back, and every other writer of the counter
    /// waits for it. Take the number as late in the unit as possible, and take several counters in a fixed order.
    /// </remarks>
    /// <param name="name">The counter name; it must be registered as <see cref="SequenceMode.GapFree" />.</param>
    /// <param name="partition">
    /// Splits the counter, for example by year. A new partition starts a new counter at the policy's start value.
    /// <see langword="null" /> or empty means no partition.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The value taken.</returns>
    /// <exception cref="ArgumentException">The name, partition, or current tenant id is invalid.</exception>
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
}
