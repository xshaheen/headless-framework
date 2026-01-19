// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Framework.Messages;
using Framework.Messages.Transactions;
using Framework.Messages.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

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
                await dbTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                break;
            case IDbContextTransaction dbContextTransaction:
                await dbContextTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                break;
        }

        await FlushAsync();
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
                await dbTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                break;
            case IDbContextTransaction dbContextTransaction:
                await dbContextTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                break;
        }
    }
}

public static class CapTransactionExtensions
{
    /// <summary>
    /// Start the CAP transaction
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
        return dbConnection.BeginTransaction(IsolationLevel.Unspecified, publisher, autoCommit);
    }

    /// <summary>
    /// Start the CAP transaction
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
        if (dbConnection.State == ConnectionState.Closed)
        {
            dbConnection.Open();
        }

        var dbTransaction = dbConnection.BeginTransaction(isolationLevel);

        publisher.Transaction = ActivatorUtilities.CreateInstance<PostgreSqlOutboxTransaction>(
            publisher.ServiceProvider
        );
        publisher.Transaction.DbTransaction = dbTransaction;
        publisher.Transaction.AutoCommit = autoCommit;

        return publisher.Transaction;
    }

    /// <summary>
    /// Start the CAP transaction
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
        return dbConnection.BeginTransactionAsync(IsolationLevel.Unspecified, publisher, autoCommit, cancellationToken);
    }

    /// <summary>
    /// Start the CAP transaction
    /// </summary>
    /// <param name="dbConnection">The <see cref="IDbConnection" />.</param>
    /// <param name="isolationLevel"><see cref="IsolationLevel"/></param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <param name="cancellationToken"></param>
    public static async ValueTask<IOutboxTransaction> BeginTransactionAsync(
        this IDbConnection dbConnection,
        IsolationLevel isolationLevel,
        IOutboxPublisher publisher,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        if (dbConnection.State == ConnectionState.Closed)
        {
            await ((DbConnection)dbConnection).OpenAsync(cancellationToken);
        }

        var dbTransaction = await ((DbConnection)dbConnection).BeginTransactionAsync(isolationLevel, cancellationToken);

        publisher.Transaction = ActivatorUtilities.CreateInstance<PostgreSqlOutboxTransaction>(
            publisher.ServiceProvider
        );
        publisher.Transaction.DbTransaction = dbTransaction;
        publisher.Transaction.AutoCommit = autoCommit;

        return publisher.Transaction;
    }

    /// <summary>
    /// Start the CAP transaction
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
        return database.BeginTransaction(IsolationLevel.Unspecified, publisher, autoCommit);
    }

    /// <summary>
    /// Start the CAP transaction
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
        var trans = database.BeginTransaction(isolationLevel);
        publisher.Transaction = ActivatorUtilities.CreateInstance<PostgreSqlOutboxTransaction>(
            publisher.ServiceProvider
        );
        publisher.Transaction.DbTransaction = trans;
        publisher.Transaction.AutoCommit = autoCommit;
        return new PostgreSqlEntityFrameworkDbTransaction(publisher.Transaction);
    }

    /// <summary>
    /// Start the CAP transaction async
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
        return database.BeginTransactionAsync(IsolationLevel.Unspecified, publisher, autoCommit, cancellationToken);
    }

    /// <summary>
    /// Start the CAP transaction async
    /// </summary>
    /// <param name="database">The <see cref="DatabaseFacade" />.</param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="isolationLevel">The <see cref="IsolationLevel" /> to use</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The <see cref="IDbContextTransaction" /> of EF DbContext transaction object.</returns>
    public static async Task<IDbContextTransaction> BeginTransactionAsync(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IOutboxPublisher publisher,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        var transaction = await database.BeginTransactionAsync(isolationLevel, cancellationToken);

        publisher.Transaction = ActivatorUtilities.CreateInstance<PostgreSqlOutboxTransaction>(
            publisher.ServiceProvider
        );
        publisher.Transaction.DbTransaction = transaction;
        publisher.Transaction.AutoCommit = autoCommit;

        return new PostgreSqlEntityFrameworkDbTransaction(publisher.Transaction);
    }
}
