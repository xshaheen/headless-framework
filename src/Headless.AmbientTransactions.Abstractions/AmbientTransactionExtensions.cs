// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;

namespace Headless.AmbientTransactions;

/// <summary>
/// Provides ambient transaction helpers for <see cref="IDbConnection" />.
/// </summary>
[PublicAPI]
public static class AmbientTransactionExtensions
{
    public static IAmbientTransaction BeginAmbientTransaction(
        this IDbConnection dbConnection,
        IAmbientTransaction transaction,
        bool autoCommit = false
    )
    {
        return dbConnection.BeginAmbientTransaction(IsolationLevel.Unspecified, transaction, autoCommit);
    }

    public static IAmbientTransaction BeginAmbientTransaction(
        this IDbConnection dbConnection,
        IsolationLevel isolationLevel,
        IAmbientTransaction transaction,
        bool autoCommit = false
    )
    {
        if (dbConnection.State == ConnectionState.Closed)
        {
            dbConnection.Open();
        }

        transaction.DbTransaction = dbConnection.BeginTransaction(isolationLevel);
        transaction.AutoCommit = autoCommit;

        return transaction;
    }

    public static ValueTask<IAmbientTransaction> BeginAmbientTransactionAsync(
        this IDbConnection dbConnection,
        IAmbientTransaction transaction,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        return dbConnection.BeginAmbientTransactionAsync(
            IsolationLevel.Unspecified,
            transaction,
            autoCommit,
            cancellationToken
        );
    }

    public static async ValueTask<IAmbientTransaction> BeginAmbientTransactionAsync(
        this IDbConnection dbConnection,
        IsolationLevel isolationLevel,
        IAmbientTransaction transaction,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        if (dbConnection.State == ConnectionState.Closed)
        {
            await ((DbConnection)dbConnection).OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.DbTransaction = await ((DbConnection)dbConnection)
            .BeginTransactionAsync(isolationLevel, cancellationToken)
            .ConfigureAwait(false);
        transaction.AutoCommit = autoCommit;

        return transaction;
    }
}
