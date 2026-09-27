// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.Hosting.Initialization;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Sequences.PostgreSql;

/// <summary>Creates the counter schema and table at host startup, once, before any call can reach them.</summary>
#pragma warning disable CA2100 // SQL text is built from the validated schema and table names plus internal column constants.
internal sealed partial class PostgreSqlSequencesStorageInitializer(
    IOptions<PostgreSqlSequencesOptions> options,
    ILogger<PostgreSqlSequencesStorageInitializer>? logger = null
) : HostedInitializer
{
    private readonly ILogger<PostgreSqlSequencesStorageInitializer> _logger =
        logger ?? NullLogger<PostgreSqlSequencesStorageInitializer>.Instance;

    protected override bool RunOnStartup => options.Value.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        await using var connection = value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // The advisory locks serialize our initializers, but a schema or object creator outside them (a consumer's EF
        // migration, other application code) can still commit the same CREATE first. That fails our transaction with
        // 42P06/42P07/42710, or 23505 on the catalog unique index when the two inserts race, and the rollback takes the
        // table with it. The conflicting creator has committed by the time we see the error, so one rerun in a fresh
        // transaction passes its IF NOT EXISTS guards and creates what the rollback discarded. A second failure is not
        // a race and propagates, so the initializer never reports success with its table missing.
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await using var command = new NpgsqlCommand(_CreateScript(value), connection, transaction);
                command.CommandTimeout = value.CommandTimeoutSeconds;
                // Keyed on the table so replicas starting together serialize their DDL; two configurations that point
                // at different tables do not wait on each other.
                command.Parameters.AddWithValue(
                    "LockResource",
                    $"headless_sequences_init:{value.Schema}.{value.TableName}"
                );
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return;
            }
            catch (PostgresException ex)
                when (attempt == 1
                    && ex.SqlState
                        is SqlErrorCodes.PostgreSql.DuplicateSchema
                            or SqlErrorCodes.PostgreSql.DuplicateTable
                            or SqlErrorCodes.PostgreSql.DuplicateObject
                            or SqlErrorCodes.PostgreSql.UniqueViolation
                )
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                LogSchemaRaceObserved(_logger, ex.SqlState, ex.MessageText);
            }
        }
    }

    private static string _CreateScript(PostgreSqlSequencesOptions options)
    {
        var table = PostgreSqlSequencesSchema.Qualified(options);

        // Key columns compare with the "C" collation, so counter names, partitions, and tenant ids match ordinally
        // (byte for byte) whatever the database's default collation is.
        return $"""
            SELECT pg_advisory_xact_lock(hashtextextended(@LockResource, 0));

            {PostgreSqlSchemaInitLock.AcquireStatement(options.Schema)}
            CREATE SCHEMA IF NOT EXISTS "{options.Schema}";

            CREATE TABLE IF NOT EXISTS {table} (
                {PostgreSqlSequencesSchema.TenantId} varchar({SequenceFieldLimits.TenantIdMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlSequencesSchema.Name} varchar({SequenceFieldLimits.NameMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlSequencesSchema.Partition} varchar({SequenceFieldLimits.PartitionMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlSequencesSchema.Value} bigint NOT NULL,
                {PostgreSqlSequencesSchema.CreatedAt} timestamptz NOT NULL,
                {PostgreSqlSequencesSchema.UpdatedAt} timestamptz NOT NULL,
                CONSTRAINT "pk_{options.TableName}" PRIMARY KEY (
                    {PostgreSqlSequencesSchema.TenantId},
                    {PostgreSqlSequencesSchema.Name},
                    {PostgreSqlSequencesSchema.Partition}
                )
            );
            """;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "PostgreSqlSequencesSchemaRaceObserved",
        Level = LogLevel.Information,
        Message = "PostgreSQL sequences initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Retrying the DDL once in a fresh transaction."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
#pragma warning restore CA2100
