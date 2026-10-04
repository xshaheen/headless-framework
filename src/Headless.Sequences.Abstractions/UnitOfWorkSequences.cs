// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Sequences;

/// <summary>
/// Provides gap-free sequence generation bound to an active unit of work through <c>unit.Sequences</c>.
/// </summary>
/// <remarks>
/// A single instance is bound per unit of work on the first read of <c>unit.Sequences</c> and stored as unit state.
/// Allocated values are committed within the unit transaction. Calling methods after completing the unit throws.
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

    /// <summary>Allocates the next value from a gap-free counter within the bound unit transaction.</summary>
    /// <remarks>
    /// The counter row lock persists until the transaction completes. Concurrent writers wait for this unit to complete.
    /// Request sequence values late in the transaction pipeline to minimize lock holding times.
    /// </remarks>
    /// <param name="name">The counter name. Must be registered as <see cref="SequenceMode.GapFree" />.</param>
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
    /// <exception cref="InvalidOperationException">
    /// The counter is not registered as gap-free, the bound unit is no longer active, or the unit cannot host the write.
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
