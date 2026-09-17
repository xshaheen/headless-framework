// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// The transactional resource a unit of work coordinates. Supplied by provider packages
/// (<c>Headless.UnitOfWork.EntityFramework</c>, <c>Headless.UnitOfWork.PostgreSql</c>, ...).
/// </summary>
/// <remarks>
/// Owned vs observed is a flag on the resource instance, never a second handle type. An owned resource's
/// transaction was begun by the unit: <see cref="CommitAsync" /> and <see cref="RollbackAsync" /> drive the real
/// transaction. An observed resource wraps a caller-committed transaction: both are expected no-ops, and the
/// unit's <see cref="IUnitOfWork.CompleteAsync" /> drains without committing.
/// </remarks>
[PublicAPI]
public interface IUnitOfWorkResource
{
    /// <summary>
    /// Gets a value indicating whether the unit owns this resource's transaction: <see langword="true" /> when
    /// the unit began the transaction and commits/rolls it back; <see langword="false" /> in observed mode,
    /// where the caller commits and the unit only coordinates the commit edge.
    /// </summary>
    bool IsOwned { get; }

    /// <summary>
    /// Gets a value indicating whether the underlying transaction has already reached a terminal state — used
    /// only to shape the forgotten-completion warning for an observed unit disposed without
    /// <see cref="IUnitOfWork.CompleteAsync" /> or <see cref="IUnitOfWork.RollbackAsync" />.
    /// </summary>
    bool IsTransactionCompleted { get; }

    /// <summary>
    /// Commits the resource's transaction. A no-op for an observed resource; the real commit for an owned one.
    /// </summary>
    /// <param name="cancellationToken">Propagates the caller's cancellation to the commit.</param>
    /// <returns>A task that completes when the commit is durable.</returns>
    ValueTask CommitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Rolls the resource's transaction back. A no-op for an observed resource; the real rollback for an
    /// owned one.
    /// </summary>
    /// <param name="cancellationToken">Propagates the caller's cancellation to the rollback.</param>
    /// <returns>A task that completes when the rollback finished.</returns>
    ValueTask RollbackAsync(CancellationToken cancellationToken);
}
