// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.Hosting.Initialization;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Idempotency.PostgreSql;

/// <summary>
/// Creates the idempotency schema, generation sequence, record table, and retention index at host startup, once, before any call can reach
/// them.
/// </summary>
#pragma warning disable CA2100 // SQL text is built from the validated schema name plus internal object and column constants.
internal sealed partial class PostgreSqlIdempotencyStorageInitializer(
    IOptions<PostgreSqlIdempotencyOptions> options,
    IOptions<IdempotencyStorageOptions> storageOptions,
    ILogger<PostgreSqlIdempotencyStorageInitializer>? logger = null
) : HostedInitializer
{
    private readonly ILogger<PostgreSqlIdempotencyStorageInitializer> _logger =
        logger ?? NullLogger<PostgreSqlIdempotencyStorageInitializer>.Instance;

    protected override bool RunOnStartup => options.Value.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        var schema = storageOptions.Value.Schema;
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
                await using var command = new NpgsqlCommand(_CreateScript(schema), connection, transaction);
                command.CommandTimeout = value.CommandTimeoutSeconds;
                // Keyed on the table so replicas starting together serialize their DDL; two configurations that point
                // at different schemas do not wait on each other.
                command.Parameters.AddWithValue(
                    "LockResource",
                    $"headless_idempotency_init:{schema}.{PostgreSqlIdempotencySchema.TableName}"
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

    private static string _CreateScript(string schema)
    {
        var table = PostgreSqlIdempotencySchema.QualifiedTable(schema);
        var sequence = PostgreSqlIdempotencySchema.QualifiedSequence(schema);
        const string t = PostgreSqlIdempotencySchema.TableName;

        // Key columns compare with the "C" collation, so keys and tenant ids match ordinally (byte for byte) whatever
        // the database's default collation is. A completed record always carries its result and contract and a
        // pending one never does, so a replay can never read a half-written outcome. A pending record names an
        // admitted attempt's generation exactly when it carries that attempt's lease expiry; a completed one keeps
        // the generation that completed it and no lease. A recovery point is whole (name, state, and contract) or
        // absent, and only a pending record keeps one, since completion clears it. One store-wide sequence issues
        // every generation, so a key admitted again after its record was purged still gets a generation above every
        // earlier attempt's. The retention index serves the purge.
        return $"""
            SELECT pg_advisory_xact_lock(hashtextextended(@LockResource, 0));

            {PostgreSqlSchemaInitLock.AcquireStatement(schema)}
            CREATE SCHEMA IF NOT EXISTS "{schema}";

            CREATE SEQUENCE IF NOT EXISTS {sequence} AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;

            CREATE TABLE IF NOT EXISTS {table} (
                {PostgreSqlIdempotencySchema.TenantId} varchar({IdempotencyFieldLimits.TenantIdMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlIdempotencySchema.Key} varchar({IdempotencyFieldLimits.KeyMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlIdempotencySchema.Status} smallint NOT NULL,
                {PostgreSqlIdempotencySchema.FingerprintAlgorithm} varchar({IdempotencyFieldLimits.FingerprintAlgorithmMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlIdempotencySchema.Fingerprint} bytea NOT NULL,
                {PostgreSqlIdempotencySchema.Generation} bigint NULL,
                {PostgreSqlIdempotencySchema.LeaseExpiresAt} timestamptz NULL,
                {PostgreSqlIdempotencySchema.Result} bytea NULL,
                {PostgreSqlIdempotencySchema.ResultContract} varchar({IdempotencyFieldLimits.ContractMaxLength}) COLLATE "C" NULL,
                {PostgreSqlIdempotencySchema.RetentionUntil} timestamptz NOT NULL,
                {PostgreSqlIdempotencySchema.RecoveryPoint} varchar({IdempotencyFieldLimits.RecoveryPointMaxLength}) COLLATE "C" NULL,
                {PostgreSqlIdempotencySchema.RecoveryState} bytea NULL,
                {PostgreSqlIdempotencySchema.RecoveryContract} varchar({IdempotencyFieldLimits.ContractMaxLength}) COLLATE "C" NULL,
                CONSTRAINT "pk_{t}" PRIMARY KEY (
                    {PostgreSqlIdempotencySchema.TenantId},
                    {PostgreSqlIdempotencySchema.Key}
                ),
                CONSTRAINT "ck_{t}_status" CHECK (
                    {PostgreSqlIdempotencySchema.Status} BETWEEN {PostgreSqlIdempotencySchema.Pending} AND {PostgreSqlIdempotencySchema.Completed}
                ),
                CONSTRAINT "ck_{t}_fingerprint" CHECK (
                    octet_length({PostgreSqlIdempotencySchema.Fingerprint}) BETWEEN 1 AND {IdempotencyFieldLimits.FingerprintMaxLength}
                ),
                CONSTRAINT "ck_{t}_result" CHECK (
                    ({PostgreSqlIdempotencySchema.Status} = {PostgreSqlIdempotencySchema.Completed})
                        = ({PostgreSqlIdempotencySchema.Result} IS NOT NULL AND {PostgreSqlIdempotencySchema.ResultContract} IS NOT NULL)
                    AND ({PostgreSqlIdempotencySchema.Result} IS NULL) = ({PostgreSqlIdempotencySchema.ResultContract} IS NULL)
                ),
                CONSTRAINT "ck_{t}_recovery" CHECK (
                    ({PostgreSqlIdempotencySchema.RecoveryPoint} IS NULL) = ({PostgreSqlIdempotencySchema.RecoveryState} IS NULL)
                    AND ({PostgreSqlIdempotencySchema.RecoveryPoint} IS NULL) = ({PostgreSqlIdempotencySchema.RecoveryContract} IS NULL)
                    AND ({PostgreSqlIdempotencySchema.RecoveryPoint} IS NULL OR {PostgreSqlIdempotencySchema.Status} = {PostgreSqlIdempotencySchema.Pending})
                    AND COALESCE(octet_length({PostgreSqlIdempotencySchema.RecoveryState}), 0) <= {IdempotencyFieldLimits.RecoveryStateMaxLength}
                ),
                CONSTRAINT "ck_{t}_lease" CHECK (
                    ({PostgreSqlIdempotencySchema.Generation} IS NULL OR {PostgreSqlIdempotencySchema.Generation} > 0)
                    AND CASE {PostgreSqlIdempotencySchema.Status}
                        WHEN {PostgreSqlIdempotencySchema.Completed} THEN
                            {PostgreSqlIdempotencySchema.Generation} IS NOT NULL AND {PostgreSqlIdempotencySchema.LeaseExpiresAt} IS NULL
                        ELSE
                            ({PostgreSqlIdempotencySchema.Generation} IS NULL) = ({PostgreSqlIdempotencySchema.LeaseExpiresAt} IS NULL)
                    END
                )
            );

            CREATE INDEX IF NOT EXISTS "ix_{t}_retention_until"
                ON {table} ({PostgreSqlIdempotencySchema.RetentionUntil});
            """;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "PostgreSqlIdempotencySchemaRaceObserved",
        Level = LogLevel.Information,
        Message = "PostgreSQL idempotency initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Retrying the DDL once in a fresh transaction."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
#pragma warning restore CA2100
