// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Data;
using System.Data.Common;
using Headless.Checks;

namespace Headless.UnitOfWork;

/// <summary>
/// The checks a relational store runs before it writes inside a caller's unit of work: the unit carries a live
/// transaction of the store's provider, on an open connection the transaction is bound to, on the database the store
/// is configured for. Every relational store makes the same checks, so they refuse the same units with the same
/// reasons.
/// </summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RelationalEnlistment
{
    /// <summary>
    /// Throws unless <paramref name="unitOfWork" /> can host a store's statements, and returns its connection and
    /// transaction. Run it on every enlisted call, not only when the unit is first judged: the caller may have ended the
    /// transaction or closed the connection since, and a statement on either would run outside the unit.
    /// </summary>
    /// <param name="unitOfWork">The active unit of work.</param>
    /// <param name="connectionType">The provider's connection type.</param>
    /// <param name="transactionType">The provider's transaction type.</param>
    /// <param name="owner">The store's package, for messages.</param>
    /// <param name="operation">What the call is, for messages (<c>"fenced lease"</c>).</param>
    /// <returns>The unit's open connection and live transaction.</returns>
    /// <exception cref="InvalidOperationException">The unit cannot host the store's statements.</exception>
    public static (DbConnection Connection, DbTransaction Transaction) RequireLive(
        IUnitOfWork unitOfWork,
        Type connectionType,
        Type transactionType,
        string owner,
        string operation
    )
    {
        Argument.IsNotNull(unitOfWork);
        Argument.IsNotNull(connectionType);
        Argument.IsNotNull(transactionType);

        UnitOfWorkTransactions.RequireTransaction<DbTransaction>(unitOfWork, operation);
        var resource = (IRelationalUnitOfWorkResource)unitOfWork.Resource!;

        if (!transactionType.IsInstanceOfType(resource.Transaction))
        {
            throw new InvalidOperationException(
                $"The unit of work carries a '{resource.Transaction.GetType().FullName}' transaction, but {owner} runs "
                    + $"enlisted {operation} calls only through a {transactionType.Name}. Begin the unit on the "
                    + "database the store is configured for."
            );
        }

        if (!connectionType.IsInstanceOfType(resource.Connection) || resource.Connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                $"The unit of work's connection is not an open {connectionType.Name}, so the {operation} call cannot "
                    + "run inside its transaction."
            );
        }

        // A committed or rolled-back transaction drops its connection, so this also refuses a finished transaction.
        if (!ReferenceEquals(resource.Transaction.Connection, resource.Connection))
        {
            throw new InvalidOperationException(
                "The unit of work's transaction is not bound to its connection, so a command on that connection "
                    + "would run outside the unit's transaction."
            );
        }

        return (resource.Connection, resource.Transaction);
    }

    /// <summary>
    /// Throws unless <paramref name="candidate" />, the unit's connection, reaches the database
    /// <paramref name="configured" /> is built for.
    /// </summary>
    /// <param name="configured">A connection built from the store's configuration; never opened here.</param>
    /// <param name="candidate">The unit of work's live connection.</param>
    /// <param name="owner">The store's package, for messages.</param>
    /// <param name="operation">What the call is, for messages.</param>
    /// <exception cref="InvalidOperationException">The two connections name different databases.</exception>
    public static void RequireSameDatabase(
        DbConnection configured,
        DbConnection candidate,
        string owner,
        string operation
    )
    {
        Argument.IsNotNull(configured);
        Argument.IsNotNull(candidate);

        if (!RelationalDatabaseIdentity.IsSameDatabase(configured, candidate))
        {
            throw new InvalidOperationException(
                $"The unit of work's connection targets database '{candidate.Database}' on '{candidate.DataSource}', "
                    + $"but {owner} is configured for database '{configured.Database}' on '{configured.DataSource}'. "
                    + $"An enlisted {operation} call runs in the unit's own transaction, so the unit must run on the "
                    + "database the store is configured for."
            );
        }
    }
}
