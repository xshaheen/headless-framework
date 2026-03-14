// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
namespace Headless.Messaging.PostgreSql;

/// <summary>
/// Provides Entity Framework-specific transaction helpers for PostgreSQL.
/// </summary>
internal static class EntityFrameworkTransactionExtensions
{
    public static IDbContextTransaction BeginEfOutboxTransaction(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IOutboxTransaction transaction,
        bool autoCommit
    )
    {
        transaction.DbTransaction = database.BeginTransaction(isolationLevel);
        transaction.AutoCommit = autoCommit;
        return new PostgreSqlEntityFrameworkDbTransaction(transaction);
    }

    public static async Task<IDbContextTransaction> BeginEfOutboxTransactionAsync(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IOutboxTransaction transaction,
        bool autoCommit,
        CancellationToken cancellationToken
    )
    {
        transaction.DbTransaction = await database.BeginTransactionAsync(isolationLevel, cancellationToken)
            .ConfigureAwait(false);
        transaction.AutoCommit = autoCommit;

        return new PostgreSqlEntityFrameworkDbTransaction(transaction);
    }
}
