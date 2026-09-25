// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Checks;
using Headless.Constants;
using Headless.UnitOfWork;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Headless.Sequences.PostgreSql;

/// <summary>
/// One <c>INSERT … ON CONFLICT DO UPDATE … RETURNING</c> per call: the first call on a key creates the row, every
/// later call adds to it under the row lock, and the statement returns the value it wrote.
/// </summary>
/// <remarks>
/// Concurrent first calls on one new key need no retry: the loser of the insert waits on the winner's unique-index
/// entry and then takes the update branch. The row lock the statement takes is held until the surrounding
/// transaction ends, which is what makes a gap-free counter serialize its writers until commit.
/// </remarks>
#pragma warning disable CA2100 // SQL text is built from the validated schema and table names plus internal column constants.
internal sealed class PostgreSqlSequenceStore(IOptions<PostgreSqlSequencesOptions> options) : ISequenceStore
{
    // A deadlock is the one failure a fresh transaction can clear on its own; the first attempt plus two retries.
    private const int _MaxAttempts = 3;

    private readonly PostgreSqlSequencesOptions _options = options.Value;
    private readonly string _incrementSql = _BuildIncrementSql(options.Value);

    public async ValueTask<long> IncrementAsync(
        SequenceKey key,
        long insertValue,
        long delta,
        CancellationToken cancellationToken = default
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var connection = _options.CreateConnection();
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                // Explicit so the statement runs at READ COMMITTED even when the server's default isolation level
                // is stricter: a stricter level would turn a concurrent first use into a serialization failure.
                await using var transaction = await connection
                    .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                    .ConfigureAwait(false);

                var value = await _ExecuteAsync(connection, transaction, key, insertValue, delta, cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return value;
            }
            catch (PostgresException ex)
                when (string.Equals(ex.SqlState, SqlErrorCodes.PostgreSql.DeadlockDetected, StringComparison.Ordinal)
                    && attempt < _MaxAttempts
                )
            {
                // The deadlock victim's transaction is already rolled back and disposed above; the next attempt
                // starts clean.
            }
        }
    }

    public void ValidateEnlistment(IRelationalUnitOfWorkResource resource)
    {
        Argument.IsNotNull(resource);

        var (connection, _) = _RequireLive(resource);

        using var configured = _options.CreateConnection();

        if (!RelationalDatabaseIdentity.IsSameDatabase(configured, connection))
        {
            throw new InvalidOperationException(
                $"The unit of work's connection targets database '{connection.Database}', but "
                    + $"Headless.Sequences.PostgreSql is configured for database '{configured.Database}'. A gap-free "
                    + "number is written into the unit's own transaction, so the unit must run on the database that "
                    + "holds the counters."
            );
        }
    }

    public async ValueTask<long> IncrementEnlistedAsync(
        IRelationalUnitOfWorkResource resource,
        SequenceKey key,
        long insertValue,
        long delta,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);

        // Re-checked here rather than trusted from validation: the caller may have ended the transaction or closed
        // the connection in between, and a statement on either would fail with a less useful message or, worse,
        // run outside the unit.
        var (connection, transaction) = _RequireLive(resource);

        // No retry: a deadlock or serialization failure has already rolled back the caller's transaction, so only
        // the unit's owner can decide whether to run the whole unit again.
        return await _ExecuteAsync(connection, transaction, key, insertValue, delta, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<long> _ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SequenceKey key,
        long insertValue,
        long delta,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(_incrementSql, connection, transaction);
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        command.Parameters.Add(
            new NpgsqlParameter<string>("TenantId", NpgsqlDbType.Varchar) { TypedValue = key.TenantId }
        );
        command.Parameters.Add(new NpgsqlParameter<string>("Name", NpgsqlDbType.Varchar) { TypedValue = key.Name });
        command.Parameters.Add(
            new NpgsqlParameter<string>("Partition", NpgsqlDbType.Varchar) { TypedValue = key.Partition }
        );
        command.Parameters.Add(
            new NpgsqlParameter<long>("InsertValue", NpgsqlDbType.Bigint) { TypedValue = insertValue }
        );
        command.Parameters.Add(new NpgsqlParameter<long>("Delta", NpgsqlDbType.Bigint) { TypedValue = delta });

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return (long)result!;
    }

    private static (NpgsqlConnection Connection, NpgsqlTransaction Transaction) _RequireLive(
        IRelationalUnitOfWorkResource resource
    )
    {
        if (resource.Transaction is not NpgsqlTransaction transaction)
        {
            throw new InvalidOperationException(
                $"The unit of work carries a '{resource.Transaction.GetType().FullName}' transaction, but "
                    + "Headless.Sequences.PostgreSql writes gap-free numbers only through an NpgsqlTransaction. Begin "
                    + "the unit on the PostgreSQL database that holds the counters."
            );
        }

        if (resource.Connection is not NpgsqlConnection { State: ConnectionState.Open } connection)
        {
            throw new InvalidOperationException(
                "The unit of work's connection is not an open NpgsqlConnection, so the gap-free number cannot be "
                    + "written inside its transaction."
            );
        }

        if (!ReferenceEquals(transaction.Connection, connection))
        {
            throw new InvalidOperationException(
                "The unit of work's transaction is not bound to its connection, so a command on that connection "
                    + "would run outside the unit's transaction."
            );
        }

        return (connection, transaction);
    }

    private static string _BuildIncrementSql(PostgreSqlSequencesOptions options)
    {
        var table = PostgreSqlSequencesSchema.Qualified(options);

        // The target is aliased so the DO UPDATE self-reference stays a plain two-part name. clock_timestamp() rather
        // than now(): a gap-free unit can hold its transaction open for a while, and the stamp records the write.
        return $"""
            INSERT INTO {table} AS t (
                {PostgreSqlSequencesSchema.TenantId},
                {PostgreSqlSequencesSchema.Name},
                {PostgreSqlSequencesSchema.Partition},
                {PostgreSqlSequencesSchema.Value},
                {PostgreSqlSequencesSchema.CreatedAt},
                {PostgreSqlSequencesSchema.UpdatedAt}
            )
            VALUES (@TenantId, @Name, @Partition, @InsertValue, clock_timestamp(), clock_timestamp())
            ON CONFLICT ({PostgreSqlSequencesSchema.TenantId}, {PostgreSqlSequencesSchema.Name}, {PostgreSqlSequencesSchema.Partition})
            DO UPDATE SET
                {PostgreSqlSequencesSchema.Value} = t.{PostgreSqlSequencesSchema.Value} + @Delta,
                {PostgreSqlSequencesSchema.UpdatedAt} = clock_timestamp()
            RETURNING {PostgreSqlSequencesSchema.Value};
            """;
    }
}
#pragma warning restore CA2100
