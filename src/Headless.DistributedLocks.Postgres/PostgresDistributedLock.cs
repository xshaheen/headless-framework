// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Npgsql;

namespace Headless.DistributedLocks.Postgres;

#pragma warning disable CA2100 // Advisory SQL text is fixed; key values are supplied as parameters.
[PublicAPI]
public static class PostgresDistributedLock
{
    public static async ValueTask AcquireWithTransactionAsync(
        PostgresAdvisoryLockKey key,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken = default
    )
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT pg_catalog.pg_advisory_xact_lock({PostgresConnectionScopedLockStorage.AddKeyParameters(command, key)})";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<bool> TryAcquireWithTransactionAsync(
        PostgresAdvisoryLockKey key,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken = default
    )
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT pg_catalog.pg_try_advisory_xact_lock({PostgresConnectionScopedLockStorage.AddKeyParameters(command, key)})";

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
    }
}
#pragma warning restore CA2100
