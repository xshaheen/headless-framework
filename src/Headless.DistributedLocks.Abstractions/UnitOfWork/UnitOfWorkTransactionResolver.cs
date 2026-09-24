// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.DistributedLocks;

/// <summary>
/// Resolves the live provider transaction a unit of work carries, for lock providers that scope a lock to it.
/// Every refusal happens before any command runs and names what the caller must change.
/// </summary>
[PublicAPI]
public static class UnitOfWorkTransactionResolver
{
    /// <summary>
    /// Returns the unit's live transaction as <typeparamref name="TTransaction" />, or throws when the unit cannot
    /// host a transaction-scoped lock for <paramref name="providerName" />.
    /// </summary>
    /// <typeparam name="TTransaction">The provider's transaction type.</typeparam>
    /// <param name="unitOfWork">The unit whose transaction the lock joins.</param>
    /// <param name="providerName">The lock provider's display name, used in refusal messages.</param>
    /// <returns>The live transaction.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unitOfWork" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    /// The unit is no longer active, its transaction already completed, it exposes no relational resource, or the
    /// resource's transaction is not a <typeparamref name="TTransaction" />.
    /// </exception>
    public static TTransaction Require<TTransaction>(IUnitOfWork unitOfWork, string providerName)
        where TTransaction : DbTransaction
    {
        Argument.IsNotNull(unitOfWork);
        Argument.IsNotNullOrWhiteSpace(providerName);

        if (unitOfWork.State != UnitOfWorkState.Active)
        {
            throw new InvalidOperationException(
                $"The unit of work is {unitOfWork.State}; a transaction-scoped {providerName} lock needs a live "
                    + "transaction to join."
            );
        }

        if (unitOfWork.Resource is not IRelationalUnitOfWorkResource relational)
        {
            throw new InvalidOperationException(
                $"The active unit of work exposes no relational resource for the {providerName} advisory lock to "
                    + $"run on. Begin the unit of work over a {providerName} connection or DbContext, or take a "
                    + "session lock through IDistributedLock instead."
            );
        }

        if (relational.IsTransactionCompleted)
        {
            throw new InvalidOperationException(
                $"The unit of work's transaction already completed; a transaction-scoped {providerName} lock "
                    + "cannot join it."
            );
        }

        return relational.Transaction as TTransaction
            ?? throw new InvalidOperationException(
                $"The unit of work's transaction is a '{relational.Transaction.GetType().FullName}', which the "
                    + $"{providerName} lock provider cannot lock inside. Register the lock provider that matches "
                    + "the unit's database."
            );
    }
}
