// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;

namespace Headless.UnitOfWork;

/// <summary>
/// Resolves the live provider transaction a unit of work carries, for bridge packages whose operation must run
/// inside it (transaction-scoped locks, gap-free sequences). Every refusal happens before any command runs and names
/// what the caller must change.
/// </summary>
[PublicAPI]
public static class UnitOfWorkTransactions
{
    /// <summary>
    /// Returns the unit's live transaction as <typeparamref name="TTransaction" />, or throws when the unit cannot
    /// host <paramref name="operation" />.
    /// </summary>
    /// <typeparam name="TTransaction">The provider's transaction type.</typeparam>
    /// <param name="unitOfWork">The unit whose transaction the operation joins.</param>
    /// <param name="operation">
    /// What joins the transaction, used in refusal messages — for example "transaction-scoped PostgreSQL lock".
    /// </param>
    /// <returns>The live transaction.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unitOfWork" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    /// The unit is no longer active, its transaction already completed, it exposes no relational resource, or the
    /// resource's transaction is not a <typeparamref name="TTransaction" />.
    /// </exception>
    public static TTransaction RequireTransaction<TTransaction>(IUnitOfWork unitOfWork, string operation)
        where TTransaction : DbTransaction
    {
        Argument.IsNotNull(unitOfWork);
        Argument.IsNotNullOrWhiteSpace(operation);

        if (unitOfWork.State != UnitOfWorkState.Active)
        {
            throw new InvalidOperationException(
                $"The unit of work is {unitOfWork.State}; a {operation} needs a live transaction to join."
            );
        }

        if (unitOfWork.Resource is not IRelationalUnitOfWorkResource relational)
        {
            throw new InvalidOperationException(
                $"The active unit of work exposes no relational resource for the {operation} to run on. Begin the "
                    + "unit of work over a database connection or DbContext."
            );
        }

        if (relational.IsTransactionCompleted)
        {
            throw new InvalidOperationException(
                $"The unit of work's transaction already completed; a {operation} cannot join it."
            );
        }

        return relational.Transaction as TTransaction
            ?? throw new InvalidOperationException(
                $"The unit of work's transaction is a '{relational.Transaction.GetType().FullName}', which the "
                    + $"{operation} cannot run inside. Register the provider that matches the unit's database."
            );
    }
}
