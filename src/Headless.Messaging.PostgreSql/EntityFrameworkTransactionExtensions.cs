// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.PostgreSql;

/// <summary>
/// Provides Entity Framework-specific transaction helpers for PostgreSQL.
/// </summary>
internal static class EntityFrameworkTransactionExtensions
{
    public static IDbContextTransaction BeginEfOutboxTransaction(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IOutboxPublisher publisher,
        bool autoCommit
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

    public static async Task<IDbContextTransaction> BeginEfOutboxTransactionAsync(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IOutboxPublisher publisher,
        bool autoCommit,
        CancellationToken cancellationToken
    )
    {
        var transaction = await database.BeginTransactionAsync(isolationLevel, cancellationToken).AnyContext();

        publisher.Transaction = ActivatorUtilities.CreateInstance<PostgreSqlOutboxTransaction>(
            publisher.ServiceProvider
        );
        publisher.Transaction.DbTransaction = transaction;
        publisher.Transaction.AutoCommit = autoCommit;

        return new PostgreSqlEntityFrameworkDbTransaction(publisher.Transaction);
    }
}
