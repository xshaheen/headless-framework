// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;

namespace Headless.Messaging.Transactions;

/// <summary>
/// Provides shared transaction extension methods for creating outbox transactions with IDbConnection.
/// This class eliminates code duplication by providing generic implementations that work with any OutboxTransactionBase-derived type.
/// </summary>
public static class OutboxTransactionExtensions
{
    /// <summary>
    /// Start an outbox transaction for an IDbConnection with the default isolation level.
    /// </summary>
    /// <param name="dbConnection">The database connection.</param>
    /// <param name="transaction">The outbox transaction instance.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published.</param>
    /// <returns>The created outbox transaction.</returns>
    public static IOutboxTransaction BeginOutboxTransaction(
        this IDbConnection dbConnection,
        IOutboxTransaction transaction,
        bool autoCommit = false
    )
    {
        return dbConnection.BeginOutboxTransaction(IsolationLevel.Unspecified, transaction, autoCommit);
    }

    /// <summary>
    /// Start an outbox transaction for an IDbConnection with a specific isolation level.
    /// </summary>
    /// <param name="dbConnection">The database connection.</param>
    /// <param name="isolationLevel">The isolation level to use.</param>
    /// <param name="transaction">The outbox transaction instance.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published.</param>
    /// <returns>The created outbox transaction.</returns>
    public static IOutboxTransaction BeginOutboxTransaction(
        this IDbConnection dbConnection,
        IsolationLevel isolationLevel,
        IOutboxTransaction transaction,
        bool autoCommit = false
    )
    {
        if (dbConnection.State == ConnectionState.Closed)
        {
            dbConnection.Open();
        }

        transaction.DbTransaction = dbConnection.BeginTransaction(isolationLevel);
        transaction.AutoCommit = autoCommit;

        return transaction;
    }

    /// <summary>
    /// Start an outbox transaction for an IDbConnection asynchronously with the default isolation level.
    /// </summary>
    /// <param name="dbConnection">The database connection.</param>
    /// <param name="transaction">The outbox transaction instance.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation containing the created outbox transaction.</returns>
    public static ValueTask<IOutboxTransaction> BeginOutboxTransactionAsync(
        this IDbConnection dbConnection,
        IOutboxTransaction transaction,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        return dbConnection.BeginOutboxTransactionAsync(
            IsolationLevel.Unspecified,
            transaction,
            autoCommit,
            cancellationToken
        );
    }

    /// <summary>
    /// Start an outbox transaction for an IDbConnection asynchronously with a specific isolation level.
    /// </summary>
    /// <param name="dbConnection">The database connection.</param>
    /// <param name="isolationLevel">The isolation level to use.</param>
    /// <param name="transaction">The outbox transaction instance.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation containing the created outbox transaction.</returns>
    public static async ValueTask<IOutboxTransaction> BeginOutboxTransactionAsync(
        this IDbConnection dbConnection,
        IsolationLevel isolationLevel,
        IOutboxTransaction transaction,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        if (dbConnection.State == ConnectionState.Closed)
        {
            await ((DbConnection)dbConnection).OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.DbTransaction = await ((DbConnection)dbConnection)
            .BeginTransactionAsync(isolationLevel, cancellationToken)
            .ConfigureAwait(false);
        transaction.AutoCommit = autoCommit;

        return transaction;
    }
}
