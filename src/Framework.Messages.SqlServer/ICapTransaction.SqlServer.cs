// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Framework.Messages.Diagnostics;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;
using Framework.Messages.Persistence;
using Framework.Messages.Transactions;
using Framework.Messages.Transport;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messages;

public class SqlServerOutboxTransaction(IDispatcher dispatcher, DiagnosticProcessorObserver diagnosticProcessor)
    : OutboxTransactionBase(dispatcher)
{
    protected override void AddToSent(MediumMessage msg)
    {
        base.AddToSent(msg);

        var dbTransaction = DbTransaction as IDbTransaction;
        if (dbTransaction == null)
        {
            if (DbTransaction is IDbContextTransaction dbContextTransaction)
            {
                dbTransaction = dbContextTransaction.GetDbTransaction();
            }

            if (dbTransaction == null)
            {
                throw new InvalidOperationException($"{nameof(DbTransaction)} is null");
            }
        }

        var transactionKey = ((SqlConnection)dbTransaction.Connection!).ClientConnectionId;

        diagnosticProcessor.TransBuffer.TryAdd(transactionKey, this);
    }

    public override void Commit()
    {
        switch (DbTransaction)
        {
            case NoopTransaction _:
                Flush();
                break;
            case IDbTransaction dbTransaction:
                dbTransaction.Commit();
                break;
            case IDbContextTransaction dbContextTransaction:
                dbContextTransaction.Commit();
                break;
        }
    }

    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        switch (DbTransaction)
        {
            case NoopTransaction _:
                await FlushAsync().ConfigureAwait(false);
                break;
            case DbTransaction dbTransaction:
                await dbTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                break;
            case IDbContextTransaction dbContextTransaction:
                await dbContextTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    public override void Rollback()
    {
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
        var dbTransaction = database.BeginTransaction(isolationLevel);
        publisher.Transaction = ActivatorUtilities.CreateInstance<SqlServerOutboxTransaction>(
            publisher.ServiceProvider
        );
        publisher.Transaction.DbTransaction = dbTransaction;
        publisher.Transaction.AutoCommit = autoCommit;
        return new CapEfDbTransaction(publisher.Transaction);
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
        var dbTransaction = await database
            .BeginTransactionAsync(isolationLevel, cancellationToken)
            .ConfigureAwait(false);

        publisher.Transaction = ActivatorUtilities.CreateInstance<SqlServerOutboxTransaction>(
            publisher.ServiceProvider
        );
        publisher.Transaction.DbTransaction = dbTransaction;
        publisher.Transaction.AutoCommit = autoCommit;

        return new CapEfDbTransaction(publisher.Transaction);
    }

    /// <summary>
    /// Start the CAP transaction
    /// </summary>
    /// <param name="dbConnection">The <see cref="IDbConnection" />.</param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <returns>The <see cref="IOutboxTransaction" /> object.</returns>
    public static IDbTransaction BeginTransaction(
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
    /// <param name="isolationLevel">The <see cref="IsolationLevel" /> to use</param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <returns>The <see cref="IOutboxTransaction" /> object.</returns>
    public static IDbTransaction BeginTransaction(
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
        publisher.Transaction = ActivatorUtilities.CreateInstance<SqlServerOutboxTransaction>(
            publisher.ServiceProvider
        );
        publisher.Transaction.DbTransaction = dbTransaction;
        publisher.Transaction.AutoCommit = autoCommit;
        return dbTransaction;
    }

    /// <summary>
    /// Start the CAP transaction
    /// </summary>
    /// <param name="dbConnection">The <see cref="IDbConnection" />.</param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The <see cref="IOutboxTransaction" /> object.</returns>
    public static Task<IDbTransaction> BeginTransactionAsync(
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
    /// <param name="isolationLevel">The <see cref="IsolationLevel" /> to use</param>
    /// <param name="publisher">The <see cref="IOutboxPublisher" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The <see cref="IOutboxTransaction" /> object.</returns>
    public static async Task<IDbTransaction> BeginTransactionAsync(
        this IDbConnection dbConnection,
        IsolationLevel isolationLevel,
        IOutboxPublisher publisher,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        if (dbConnection.State == ConnectionState.Closed)
        {
            await ((DbConnection)dbConnection).OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        var dbTransaction = await ((DbConnection)dbConnection)
            .BeginTransactionAsync(isolationLevel, cancellationToken)
            .ConfigureAwait(false);

        publisher.Transaction = ActivatorUtilities.CreateInstance<SqlServerOutboxTransaction>(
            publisher.ServiceProvider
        );
        publisher.Transaction.DbTransaction = dbTransaction;
        publisher.Transaction.AutoCommit = autoCommit;

        return dbTransaction;
    }
}
