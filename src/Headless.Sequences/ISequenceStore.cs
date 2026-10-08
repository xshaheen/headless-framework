// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;

namespace Headless.Sequences;

/// <summary>
/// Defines the provider seam behind sequences: one atomic upsert-increment per call, either on the provider's
/// own connection or on a unit of work's transaction.
/// </summary>
/// <remarks>
/// Each operation creates the row with <c>insertValue</c> when the key has none, and otherwise
/// adds <c>delta</c> to the stored value; it returns the stored value after the write. Policy,
/// mode, and argument checks happen before a call reaches the store. A provider package registers the
/// implementation; application code never calls it.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISequenceStore
{
    /// <summary>Increments the counter on the provider's own connection and commits before returning.</summary>
    /// <param name="key">The counter's key.</param>
    /// <param name="insertValue">The value stored when the key has no row yet.</param>
    /// <param name="delta">The amount added to an existing row.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The counter's value after the increment.</returns>
    ValueTask<long> IncrementAsync(
        SequenceKey key,
        long insertValue,
        long delta,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Throws when <paramref name="unitOfWork" /> cannot host this provider's write: it has no live
    /// transaction, its transaction belongs to another provider, it targets a different database than the
    /// one configured, or its connection is not open.
    /// </summary>
    /// <param name="unitOfWork">The active unit of work.</param>
    /// <exception cref="ArgumentNullException"><paramref name="unitOfWork"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="unitOfWork"/> cannot host the write.</exception>
    void ValidateEnlistment(IUnitOfWork unitOfWork);

    /// <summary>
    /// Increments the counter on <paramref name="unitOfWork" />'s connection and inside its transaction,
    /// without committing. The row stays locked until that transaction ends.
    /// </summary>
    /// <param name="unitOfWork">The unit of work, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The counter's key.</param>
    /// <param name="insertValue">The value stored when the key has no row yet.</param>
    /// <param name="delta">The amount added to an existing row.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The counter's value after the increment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unitOfWork"/> is <see langword="null"/>.</exception>
    ValueTask<long> IncrementEnlistedAsync(
        IUnitOfWork unitOfWork,
        SequenceKey key,
        long insertValue,
        long delta,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Moves the counter at <paramref name="key" /> forward to <paramref name="value" /> inside
    /// <paramref name="unitOfWork" />'s transaction when the stored value is lower, creating the counter at
    /// <paramref name="baseline" /> first when it does not exist. The counter's row stays locked until the
    /// transaction ends.
    /// </summary>
    /// <param name="unitOfWork">The unit of work, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The counter key.</param>
    /// <param name="value">The reported value.</param>
    /// <param name="baseline">The value a new counter starts at: one step below the first value expected.</param>
    /// <param name="cancellationToken">Token used to cancel the database commands.</param>
    /// <returns>The value stored before this call, read under the row lock.</returns>
    ValueTask<long> AdvanceEnlistedAsync(
        IUnitOfWork unitOfWork,
        SequenceKey key,
        long value,
        long baseline,
        CancellationToken cancellationToken = default
    );
}
