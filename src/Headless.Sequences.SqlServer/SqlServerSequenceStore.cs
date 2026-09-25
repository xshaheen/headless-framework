// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Checks;
using Headless.Constants;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Sequences.SqlServer;

/// <summary>
/// One batch per call: an <c>UPDATE … WITH (UPDLOCK, HOLDLOCK)</c> adds to an existing row, and when none matched an
/// <c>INSERT</c> in the same transaction creates it. Both write the new value into a table variable that one trailing
/// <c>SELECT</c> returns.
/// </summary>
/// <remarks>
/// <para>
/// On a new key <c>HOLDLOCK</c> takes a key-range lock on the gap of the clustered primary key that would hold the
/// key, so concurrent first calls queue behind the first one and then take the update branch: no duplicate-key error
/// and no retry. The same range lock also makes first use of any other new key that sorts into that gap wait, and
/// in a gap-free unit it is held until the unit's transaction ends.
/// </para>
/// <para>
/// The batch deliberately has no <c>TRY/CATCH</c>. A caught duplicate-key error inside a caller transaction that runs
/// with <c>XACT_ABORT ON</c> dooms that transaction, so the insert must never be allowed to fail and be absorbed.
/// </para>
/// </remarks>
internal sealed class SqlServerSequenceStore(IOptions<SqlServerSequencesOptions> options) : ISequenceStore
{
    // A deadlock is the one failure a fresh transaction can clear on its own; the first attempt plus two retries.
    private const int _MaxAttempts = 3;

    private readonly SqlServerSequencesOptions _options = options.Value;
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
                // Explicit so the batch runs at READ COMMITTED even when the session default is stricter; the
                // HOLDLOCK hint already gives the one table access the range locking it needs.
                await using var transaction = (SqlTransaction)
                    await connection
                        .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                        .ConfigureAwait(false);

                var value = await _ExecuteAsync(connection, transaction, key, insertValue, delta, cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return value;
            }
            catch (SqlException ex) when (ex.Number == SqlErrorCodes.SqlServer.DeadlockVictim && attempt < _MaxAttempts)
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
                $"The unit of work's connection targets database '{connection.Database}' on '{connection.DataSource}', "
                    + $"but Headless.Sequences.SqlServer is configured for database '{configured.Database}' on "
                    + $"'{configured.DataSource}'. A gap-free number is written into the unit's own transaction, so "
                    + "the unit must run on the database that holds the counters."
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

        // No retry: a deadlock has already rolled back the caller's transaction, so only the unit's owner can decide
        // whether to run the whole unit again.
        return await _ExecuteAsync(connection, transaction, key, insertValue, delta, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<long> _ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SequenceKey key,
        long insertValue,
        long delta,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(_incrementSql, connection, transaction);
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        command.Parameters.Add(
            new SqlParameter("TenantId", SqlDbType.NVarChar, SequenceFieldLimits.TenantIdMaxLength)
            {
                Value = key.TenantId,
            }
        );
        command.Parameters.Add(
            new SqlParameter("Name", SqlDbType.NVarChar, SequenceFieldLimits.NameMaxLength) { Value = key.Name }
        );
        command.Parameters.Add(
            new SqlParameter("Partition", SqlDbType.NVarChar, SequenceFieldLimits.PartitionMaxLength)
            {
                Value = key.Partition,
            }
        );
        command.Parameters.Add(new SqlParameter("InsertValue", SqlDbType.BigInt) { Value = insertValue });
        command.Parameters.Add(new SqlParameter("Delta", SqlDbType.BigInt) { Value = delta });

        object? result;

        try
        {
            result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        // SqlClient reports a command it cancelled mid-flight as a SqlException ("Operation cancelled by user");
        // surface it as the cancellation the caller asked for.
        catch (SqlException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }

        return (long)result!;
    }

    private static (SqlConnection Connection, SqlTransaction Transaction) _RequireLive(
        IRelationalUnitOfWorkResource resource
    )
    {
        if (resource.Transaction is not SqlTransaction transaction)
        {
            throw new InvalidOperationException(
                $"The unit of work carries a '{resource.Transaction.GetType().FullName}' transaction, but "
                    + "Headless.Sequences.SqlServer writes gap-free numbers only through a SqlTransaction. Begin the "
                    + "unit on the SQL Server database that holds the counters."
            );
        }

        if (resource.Connection is not SqlConnection { State: ConnectionState.Open } connection)
        {
            throw new InvalidOperationException(
                "The unit of work's connection is not an open SqlConnection, so the gap-free number cannot be "
                    + "written inside its transaction."
            );
        }

        // A committed or rolled-back SqlTransaction drops its connection, so this also refuses a finished transaction.
        if (!ReferenceEquals(transaction.Connection, connection))
        {
            throw new InvalidOperationException(
                "The unit of work's transaction is not bound to its connection, so a command on that connection "
                    + "would run outside the unit's transaction."
            );
        }

        return (connection, transaction);
    }

    private static string _BuildIncrementSql(SqlServerSequencesOptions options)
    {
        var table = SqlServerSequencesSchema.Qualified(options);

        // A bare OUTPUT on the UPDATE would return an empty first result set on a new key, and ExecuteScalar would
        // read null; routing both branches through @allocated leaves exactly one result set. SYSUTCDATETIME() stamps
        // the write itself: a gap-free unit can hold its transaction open for a while.
        //
        // No SET options in the batch: a SET issued in an ad-hoc batch outlives it for the rest of the session, and in
        // a gap-free call that session is the caller's.
        return $"""
            DECLARE @allocated table ([value] bigint NOT NULL);

            UPDATE {table} WITH (UPDLOCK, HOLDLOCK)
            SET {SqlServerSequencesSchema.Value} = {SqlServerSequencesSchema.Value} + @Delta,
                {SqlServerSequencesSchema.UpdatedAt} = SYSUTCDATETIME()
            OUTPUT inserted.{SqlServerSequencesSchema.Value} INTO @allocated
            WHERE {SqlServerSequencesSchema.TenantId} = @TenantId
              AND {SqlServerSequencesSchema.Name} = @Name
              AND {SqlServerSequencesSchema.Partition} = @Partition;

            IF NOT EXISTS (SELECT 1 FROM @allocated)
                INSERT INTO {table} (
                    {SqlServerSequencesSchema.TenantId},
                    {SqlServerSequencesSchema.Name},
                    {SqlServerSequencesSchema.Partition},
                    {SqlServerSequencesSchema.Value},
                    {SqlServerSequencesSchema.CreatedAt},
                    {SqlServerSequencesSchema.UpdatedAt}
                )
                OUTPUT inserted.{SqlServerSequencesSchema.Value} INTO @allocated
                VALUES (@TenantId, @Name, @Partition, @InsertValue, SYSUTCDATETIME(), SYSUTCDATETIME());

            SELECT TOP (1) [value] FROM @allocated;
            """;
    }
}
