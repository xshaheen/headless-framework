// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Messaging.SqlServer;

/// <summary>
/// Provides Entity Framework-specific transaction helpers for SQL Server.
/// </summary>
internal static class EntityFrameworkTransactionExtensions
{
    public static IDbContextTransaction BeginEntityFrameworkOutboxTransaction(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IOutboxTransaction transaction,
        bool autoCommit
    )
    {
        transaction.DbTransaction = database.BeginTransaction(isolationLevel);
        transaction.AutoCommit = autoCommit;
        return new SqlServerEntityFrameworkDbTransaction(transaction);
    }

    public static async Task<IDbContextTransaction> BeginEntityFrameworkOutboxTransactionAsync(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IOutboxTransaction transaction,
        bool autoCommit,
        CancellationToken cancellationToken
    )
    {
        transaction.DbTransaction = await database
            .BeginTransactionAsync(isolationLevel, cancellationToken)
            .ConfigureAwait(false);
        transaction.AutoCommit = autoCommit;

        return new SqlServerEntityFrameworkDbTransaction(transaction);
    }
}
