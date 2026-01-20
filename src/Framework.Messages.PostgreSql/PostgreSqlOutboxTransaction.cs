// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Framework.Messages.Transactions;
using Framework.Messages.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Framework.Messages;

public class PostgreSqlOutboxTransaction(IDispatcher dispatcher) : OutboxTransactionBase(dispatcher)
{
    public override void Commit()
    {
        Debug.Assert(DbTransaction != null);

        switch (DbTransaction)
        {
            case IDbTransaction dbTransaction:
                dbTransaction.Commit();
                break;
            case IDbContextTransaction dbContextTransaction:
                dbContextTransaction.Commit();
                break;
        }

        Flush();
    }

    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Debug.Assert(DbTransaction != null);

        switch (DbTransaction)
        {
            case DbTransaction dbTransaction:
                await dbTransaction.CommitAsync(cancellationToken).AnyContext();
                break;
            case IDbContextTransaction dbContextTransaction:
                await dbContextTransaction.CommitAsync(cancellationToken).AnyContext();
                break;
        }

        await FlushAsync(cancellationToken).AnyContext();
    }

    public override void Rollback()
    {
        Debug.Assert(DbTransaction != null);

        switch (DbTransaction)
        {
            case IDbTransaction dbTransaction:
                dbTransaction.Rollback();
                break;
            case IDbContextTransaction dbContextTransaction:
                dbContextTransaction.Rollback();
                break;
        }
    }

    public override async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        Debug.Assert(DbTransaction != null);

        switch (DbTransaction)
        {
            case DbTransaction dbTransaction:
                await dbTransaction.RollbackAsync(cancellationToken).AnyContext();
                break;
            case IDbContextTransaction dbContextTransaction:
                await dbContextTransaction.RollbackAsync(cancellationToken).AnyContext();
                break;
        }
    }
}

public static class PostgreSqlTransactionExtensions
{
    /// <summary>
    /// Start the outbox transaction
    /// </summary>
    /// <param name="dbConnection">The <see cref="IDbConnection" />.</param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    public static IOutboxTransaction BeginTransaction(
        this IDbConnection dbConnection,
        IOutboxPublisher publisher,
        bool autoCommit = false
    )
    {
        return dbConnection.BeginOutboxTransaction<PostgreSqlOutboxTransaction>(publisher, autoCommit);
    }

    /// <summary>
    /// Start the outbox transaction
    /// </summary>
    /// <param name="dbConnection">The <see cref="IDbConnection" />.</param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <param name="isolationLevel"><see cref="IsolationLevel"/></param>
    public static IOutboxTransaction BeginTransaction(
        this IDbConnection dbConnection,
        IsolationLevel isolationLevel,
        IOutboxPublisher publisher,
        bool autoCommit = false
    )
    {
        return dbConnection.BeginOutboxTransaction<PostgreSqlOutboxTransaction>(isolationLevel, publisher, autoCommit);
    }

    /// <summary>
    /// Start the outbox transaction
    /// </summary>
    /// <param name="dbConnection">The <see cref="IDbConnection" />.</param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <param name="cancellationToken"></param>
    public static ValueTask<IOutboxTransaction> BeginTransactionAsync(
        this IDbConnection dbConnection,
        IOutboxPublisher publisher,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        return dbConnection.BeginOutboxTransactionAsync<PostgreSqlOutboxTransaction>(
            publisher,
            autoCommit,
            cancellationToken
        );
    }

    /// <summary>
    /// Start the outbox transaction
    /// </summary>
    /// <param name="dbConnection">The <see cref="IDbConnection" />.</param>
    /// <param name="isolationLevel"><see cref="IsolationLevel"/></param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <param name="cancellationToken"></param>
    public static ValueTask<IOutboxTransaction> BeginTransactionAsync(
        this IDbConnection dbConnection,
        IsolationLevel isolationLevel,
        IOutboxPublisher publisher,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        return dbConnection.BeginOutboxTransactionAsync<PostgreSqlOutboxTransaction>(
            isolationLevel,
            publisher,
            autoCommit,
            cancellationToken
        );
    }

    /// <summary>
    /// Start the outbox transaction
    /// </summary>
    /// <param name="database">The <see cref="DatabaseFacade" />.</param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <returns>The <see cref="IDbContextTransaction" /> of EF DbContext transaction object.</returns>
    public static IDbContextTransaction BeginTransaction(
        this DatabaseFacade database,
        IOutboxPublisher publisher,
        bool autoCommit = false
    )
    {
        return database.BeginEfOutboxTransaction(IsolationLevel.Unspecified, publisher, autoCommit);
    }

    /// <summary>
    /// Start the outbox transaction
    /// </summary>
    /// <param name="database">The <see cref="DatabaseFacade" />.</param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="isolationLevel">The <see cref="IsolationLevel" /> to use</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <returns>The <see cref="IDbContextTransaction" /> of EF DbContext transaction object.</returns>
    public static IDbContextTransaction BeginTransaction(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IOutboxPublisher publisher,
        bool autoCommit = false
    )
    {
        return database.BeginEfOutboxTransaction(isolationLevel, publisher, autoCommit);
    }

    /// <summary>
    /// Start the outbox transaction async
    /// </summary>
    /// <param name="database">The <see cref="DatabaseFacade" />.</param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The <see cref="IDbContextTransaction" /> of EF DbContext transaction object.</returns>
    public static Task<IDbContextTransaction> BeginTransactionAsync(
        this DatabaseFacade database,
        IOutboxPublisher publisher,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        return database.BeginEfOutboxTransactionAsync(
            IsolationLevel.Unspecified,
            publisher,
            autoCommit,
            cancellationToken
        );
    }

    /// <summary>
    /// Start the outbox transaction async
    /// </summary>
    /// <param name="database">The <see cref="DatabaseFacade" />.</param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="isolationLevel">The <see cref="IsolationLevel" /> to use</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The <see cref="IDbContextTransaction" /> of EF DbContext transaction object.</returns>
    public static Task<IDbContextTransaction> BeginTransactionAsync(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IOutboxPublisher publisher,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        return database.BeginEfOutboxTransactionAsync(isolationLevel, publisher, autoCommit, cancellationToken);
    }
}
