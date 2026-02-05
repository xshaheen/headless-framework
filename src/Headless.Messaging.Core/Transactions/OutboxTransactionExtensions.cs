// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;

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
    /// <typeparam name="TTransaction">The concrete transaction type to create.</typeparam>
    /// <param name="dbConnection">The database connection.</param>
    /// <param name="publisher">The outbox publisher.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published.</param>
    /// <returns>The created outbox transaction.</returns>
    public static IOutboxTransaction BeginOutboxTransaction<TTransaction>(
        this IDbConnection dbConnection,
        IOutboxPublisher publisher,
        bool autoCommit = false
    )
        where TTransaction : OutboxTransaction
    {
        return dbConnection.BeginOutboxTransaction<TTransaction>(IsolationLevel.Unspecified, publisher, autoCommit);
    }

    /// <summary>
    /// Start an outbox transaction for an IDbConnection with a specific isolation level.
    /// </summary>
    /// <typeparam name="TTransaction">The concrete transaction type to create.</typeparam>
    /// <param name="dbConnection">The database connection.</param>
    /// <param name="isolationLevel">The isolation level to use.</param>
    /// <param name="publisher">The outbox publisher.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published.</param>
    /// <returns>The created outbox transaction.</returns>
    public static IOutboxTransaction BeginOutboxTransaction<TTransaction>(
        this IDbConnection dbConnection,
        IsolationLevel isolationLevel,
        IOutboxPublisher publisher,
        bool autoCommit = false
    )
        where TTransaction : OutboxTransaction
    {
        if (dbConnection.State == ConnectionState.Closed)
        {
            dbConnection.Open();
        }

        var dbTransaction = dbConnection.BeginTransaction(isolationLevel);

        publisher.Transaction = ActivatorUtilities.CreateInstance<TTransaction>(publisher.ServiceProvider);
        publisher.Transaction.DbTransaction = dbTransaction;
        publisher.Transaction.AutoCommit = autoCommit;

        return publisher.Transaction;
    }

    /// <summary>
    /// Start an outbox transaction for an IDbConnection asynchronously with the default isolation level.
    /// </summary>
    /// <typeparam name="TTransaction">The concrete transaction type to create.</typeparam>
    /// <param name="dbConnection">The database connection.</param>
    /// <param name="publisher">The outbox publisher.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation containing the created outbox transaction.</returns>
    public static ValueTask<IOutboxTransaction> BeginOutboxTransactionAsync<TTransaction>(
        this IDbConnection dbConnection,
        IOutboxPublisher publisher,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
        where TTransaction : OutboxTransaction
    {
        return dbConnection.BeginOutboxTransactionAsync<TTransaction>(
            IsolationLevel.Unspecified,
            publisher,
            autoCommit,
            cancellationToken
        );
    }

    /// <summary>
    /// Start an outbox transaction for an IDbConnection asynchronously with a specific isolation level.
    /// </summary>
    /// <typeparam name="TTransaction">The concrete transaction type to create.</typeparam>
    /// <param name="dbConnection">The database connection.</param>
    /// <param name="isolationLevel">The isolation level to use.</param>
    /// <param name="publisher">The outbox publisher.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation containing the created outbox transaction.</returns>
    public static async ValueTask<IOutboxTransaction> BeginOutboxTransactionAsync<TTransaction>(
        this IDbConnection dbConnection,
        IsolationLevel isolationLevel,
        IOutboxPublisher publisher,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
        where TTransaction : OutboxTransaction
    {
        if (dbConnection.State == ConnectionState.Closed)
        {
            await ((DbConnection)dbConnection).OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        var dbTransaction = await ((DbConnection)dbConnection)
            .BeginTransactionAsync(isolationLevel, cancellationToken)
            .ConfigureAwait(false);

        publisher.Transaction = ActivatorUtilities.CreateInstance<TTransaction>(publisher.ServiceProvider);
        publisher.Transaction.DbTransaction = dbTransaction;
        publisher.Transaction.AutoCommit = autoCommit;

        return publisher.Transaction;
    }
}
