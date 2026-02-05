// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.SqlServer;

/// <summary>
/// Provides Entity Framework-specific transaction helpers for SQL Server.
/// </summary>
internal static class EntityFrameworkTransactionExtensions
{
    public static IDbContextTransaction BeginEntityFrameworkOutboxTransaction(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IOutboxPublisher publisher,
        bool autoCommit
    )
    {
        var dbTransaction = database.BeginTransaction(isolationLevel);
        publisher.Transaction = ActivatorUtilities.CreateInstance<SqlServerOutboxTransaction>(
            publisher.ServiceProvider
        );
        publisher.Transaction.DbTransaction = dbTransaction;
        publisher.Transaction.AutoCommit = autoCommit;
        return new SqlServerEntityFrameworkDbTransaction(publisher.Transaction);
    }

    public static async Task<IDbContextTransaction> BeginEntityFrameworkOutboxTransactionAsync(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IOutboxPublisher publisher,
        bool autoCommit,
        CancellationToken cancellationToken
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

        return new SqlServerEntityFrameworkDbTransaction(publisher.Transaction);
    }
}
