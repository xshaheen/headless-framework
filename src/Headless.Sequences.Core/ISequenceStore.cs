// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;

namespace Headless.Sequences;

/// <summary>
/// Defines the low-level database store contract for executing atomic upsert-increment operations.
/// </summary>
/// <remarks>
/// Operations insert an initial value when no row exists or add a delta to existing rows,
/// returning the resulting value. Implementations are registered by provider packages.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISequenceStore
{
    /// <summary>Increments the counter on a dedicated database connection and commits before returning.</summary>
    /// <param name="key">The counter key.</param>
    /// <param name="insertValue">The initial value when creating a row.</param>
    /// <param name="delta">The increment added to an existing row.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The counter value after the increment.</returns>
    ValueTask<long> IncrementAsync(
        SequenceKey key,
        long insertValue,
        long delta,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Validates that <paramref name="unitOfWork" /> can host this provider writes.
    /// </summary>
    /// <param name="unitOfWork">The active unit of work.</param>
    /// <exception cref="ArgumentNullException"><paramref name="unitOfWork"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="unitOfWork"/> cannot host the write.</exception>
    void ValidateEnlistment(IUnitOfWork unitOfWork);

    /// <summary>
    /// Increments the counter within the <paramref name="unitOfWork" /> transaction without committing.
    /// </summary>
    /// <param name="unitOfWork">The active unit of work accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The counter key.</param>
    /// <param name="insertValue">The initial value when creating a row.</param>
    /// <param name="delta">The increment added to an existing row.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The counter value after the increment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unitOfWork"/> is <see langword="null"/>.</exception>
    ValueTask<long> IncrementEnlistedAsync(
        IUnitOfWork unitOfWork,
        SequenceKey key,
        long insertValue,
        long delta,
        CancellationToken cancellationToken = default
    );
}
