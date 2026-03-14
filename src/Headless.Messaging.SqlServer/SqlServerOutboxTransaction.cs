// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.SqlServer.Diagnostics;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Messaging.SqlServer;

public sealed class SqlServerOutboxTransaction(
    IDispatcher dispatcher,
    IOutboxTransactionAccessor accessor,
    DiagnosticProcessorObserver diagnosticProcessor
) : OutboxTransaction(dispatcher, accessor)
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
            case NoopTransaction:
                await FlushAsync(cancellationToken).ConfigureAwait(false);
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

public static class SqlServerTransactionExtensions
{
    /// <summary>
    /// Start the outbox transaction
    /// </summary>
    /// <param name="database">The <see cref="DatabaseFacade" />.</param>
    /// <param name="transaction">The <see cref="IOutboxTransaction" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <returns>The <see cref="IDbContextTransaction" /> of EF DbContext transaction object.</returns>
    public static IDbContextTransaction BeginTransaction(
        this DatabaseFacade database,
        IOutboxTransaction transaction,
        bool autoCommit = false
    )
    {
        return database.BeginEntityFrameworkOutboxTransaction(IsolationLevel.Unspecified, transaction, autoCommit);
    }

    /// <summary>
    /// Start the outbox transaction
    /// </summary>
    /// <param name="database">The <see cref="DatabaseFacade" />.</param>
    /// <param name="transaction">The <see cref="IOutboxTransaction" />.</param>
    /// <param name="isolationLevel">The <see cref="IsolationLevel" /> to use</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <returns>The <see cref="IDbContextTransaction" /> of EF DbContext transaction object.</returns>
    public static IDbContextTransaction BeginTransaction(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IOutboxTransaction transaction,
        bool autoCommit = false
    )
    {
        return database.BeginEntityFrameworkOutboxTransaction(isolationLevel, transaction, autoCommit);
    }

    /// <summary>
    /// Start the outbox transaction async
    /// </summary>
    /// <param name="database">The <see cref="DatabaseFacade" />.</param>
    /// <param name="transaction">The <see cref="IOutboxTransaction" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The <see cref="IDbContextTransaction" /> of EF DbContext transaction object.</returns>
    public static Task<IDbContextTransaction> BeginTransactionAsync(
        this DatabaseFacade database,
        IOutboxTransaction transaction,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        return database.BeginEntityFrameworkOutboxTransactionAsync(
            IsolationLevel.Unspecified,
            transaction,
            autoCommit,
            cancellationToken
        );
    }

    /// <summary>
    /// Start the outbox transaction async
    /// </summary>
    /// <param name="database">The <see cref="DatabaseFacade" />.</param>
    /// <param name="transaction">The <see cref="IOutboxTransaction" />.</param>
    /// <param name="isolationLevel">The <see cref="IsolationLevel" /> to use</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The <see cref="IDbContextTransaction" /> of EF DbContext transaction object.</returns>
    public static Task<IDbContextTransaction> BeginTransactionAsync(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IOutboxTransaction transaction,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        return database.BeginEntityFrameworkOutboxTransactionAsync(
            isolationLevel,
            transaction,
            autoCommit,
            cancellationToken
        );
    }

    /// <summary>
    /// Start the outbox transaction
    /// </summary>
    /// <param name="dbConnection">The <see cref="IDbConnection" />.</param>
    /// <param name="transaction">The <see cref="IOutboxTransaction" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <returns>The <see cref="IOutboxTransaction" /> object.</returns>
    public static IOutboxTransaction BeginTransaction(
        this IDbConnection dbConnection,
        IOutboxTransaction transaction,
        bool autoCommit = false
    )
    {
        return dbConnection.BeginOutboxTransaction(transaction, autoCommit);
    }

    /// <summary>
    /// Start the outbox transaction
    /// </summary>
    /// <param name="dbConnection">The <see cref="IDbConnection" />.</param>
    /// <param name="isolationLevel">The <see cref="IsolationLevel" /> to use</param>
    /// <param name="transaction">The <see cref="IOutboxTransaction" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <returns>The <see cref="IOutboxTransaction" /> object.</returns>
    public static IOutboxTransaction BeginTransaction(
        this IDbConnection dbConnection,
        IsolationLevel isolationLevel,
        IOutboxTransaction transaction,
        bool autoCommit = false
    )
    {
        return dbConnection.BeginOutboxTransaction(isolationLevel, transaction, autoCommit);
    }

    /// <summary>
    /// Start the outbox transaction
    /// </summary>
    /// <param name="dbConnection">The <see cref="IDbConnection" />.</param>
    /// <param name="transaction">The <see cref="IOutboxTransaction" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The <see cref="IOutboxTransaction" /> object.</returns>
    public static Task<IOutboxTransaction> BeginTransactionAsync(
        this IDbConnection dbConnection,
        IOutboxTransaction transaction,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        return dbConnection.BeginOutboxTransactionAsync(transaction, autoCommit, cancellationToken).AsTask();
    }

    /// <summary>
    /// Start the outbox transaction
    /// </summary>
    /// <param name="dbConnection">The <see cref="IDbConnection" />.</param>
    /// <param name="isolationLevel">The <see cref="IsolationLevel" /> to use</param>
    /// <param name="transaction">The <see cref="IOutboxTransaction" />.</param>
    /// <param name="autoCommit">Whether the transaction is automatically committed when the message is published</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The <see cref="IOutboxTransaction" /> object.</returns>
    public static Task<IOutboxTransaction> BeginTransactionAsync(
        this IDbConnection dbConnection,
        IsolationLevel isolationLevel,
        IOutboxTransaction transaction,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        return dbConnection
            .BeginOutboxTransactionAsync(isolationLevel, transaction, autoCommit, cancellationToken)
            .AsTask();
    }
}
