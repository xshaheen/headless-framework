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
        var connection =
            transaction.Connection
            ?? throw new InvalidOperationException(
                "The transaction has no associated open connection (already committed, rolled back, or disposed)."
            );

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT pg_catalog.pg_advisory_xact_lock({key.AddKeyParameters(command)})";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<bool> TryAcquireWithTransactionAsync(
        PostgresAdvisoryLockKey key,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken = default
    )
    {
        var connection =
            transaction.Connection
            ?? throw new InvalidOperationException(
                "The transaction has no associated open connection (already committed, rolled back, or disposed)."
            );

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT pg_catalog.pg_try_advisory_xact_lock({key.AddKeyParameters(command)})";

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
    }
}
#pragma warning restore CA2100
