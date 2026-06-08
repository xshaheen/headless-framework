// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.AmbientTransactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.AmbientTransactions.EntityFramework;

/// <summary>
/// Entity Framework Core helpers for beginning ambient transactions.
/// </summary>
[PublicAPI]
public static class EfAmbientTransactionExtensions
{
    public static IAmbientTransaction AsAmbientTransaction(
        this IDbContextTransaction dbContextTransaction,
        ICurrentAmbientTransaction currentAmbientTransaction,
        bool autoCommit = false
    )
    {
        return new EfAmbientTransaction(dbContextTransaction, currentAmbientTransaction, autoCommit);
    }

    public static IAmbientTransaction BeginAmbientTransaction(
        this DatabaseFacade database,
        IAmbientTransaction transaction,
        bool autoCommit = false
    )
    {
        return database.BeginAmbientTransaction(IsolationLevel.Unspecified, transaction, autoCommit);
    }

    public static IAmbientTransaction BeginAmbientTransaction(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IAmbientTransaction transaction,
        bool autoCommit = false
    )
    {
        transaction.DbTransaction = database.BeginTransaction(isolationLevel);
        transaction.AutoCommit = autoCommit;

        return transaction;
    }

    public static Task<IAmbientTransaction> BeginAmbientTransactionAsync(
        this DatabaseFacade database,
        IAmbientTransaction transaction,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        return database.BeginAmbientTransactionAsync(
            IsolationLevel.Unspecified,
            transaction,
            autoCommit,
            cancellationToken
        );
    }

    public static async Task<IAmbientTransaction> BeginAmbientTransactionAsync(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        IAmbientTransaction transaction,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        transaction.DbTransaction = await database
            .BeginTransactionAsync(isolationLevel, cancellationToken)
            .ConfigureAwait(false);
        transaction.AutoCommit = autoCommit;

        return transaction;
    }
}
