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
    extension(DatabaseFacade database)
    {
        public IDbContextTransaction BeginEntityFrameworkOutboxTransaction(
            IsolationLevel isolationLevel,
            IOutboxTransaction transaction,
            bool autoCommit
        )
        {
            transaction.DbTransaction = database.BeginTransaction(isolationLevel);
            transaction.AutoCommit = autoCommit;
            return new SqlServerEntityFrameworkDbTransaction(transaction);
        }

        public async Task<IDbContextTransaction> BeginEntityFrameworkOutboxTransactionAsync(
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
}
