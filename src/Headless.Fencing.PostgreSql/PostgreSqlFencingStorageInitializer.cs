// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.Hosting.Initialization;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Fencing.PostgreSql;

/// <summary>
/// Creates the fencing schema, lease table, indexes, and generation sequence at host startup, once, before any call
/// can reach them.
/// </summary>
#pragma warning disable CA2100 // SQL text is built from the validated schema name plus internal object and column constants.
internal sealed partial class PostgreSqlFencingStorageInitializer(
    IOptions<PostgreSqlFencingOptions> options,
    IOptions<FencingStorageOptions> storageOptions,
    ILogger<PostgreSqlFencingStorageInitializer>? logger = null
) : HostedInitializer
{
    private readonly ILogger<PostgreSqlFencingStorageInitializer> _logger =
        logger ?? NullLogger<PostgreSqlFencingStorageInitializer>.Instance;

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
                    $"headless_fencing_init:{schema}.{PostgreSqlFencingSchema.TableName}"
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
        var table = PostgreSqlFencingSchema.QualifiedTable(schema);
        var sequence = PostgreSqlFencingSchema.QualifiedSequence(schema);

        // Key columns compare with the "C" collation, so kinds, resources, and tenant ids match ordinally (byte for
        // byte) whatever the database's default collation is. One store-wide sequence issues every generation, so a
        // lease granted again after its row was purged still gets a generation above every earlier one. The active
        // index serves the sweep's keyset walk in (expires_at, tenant_id, resource) order; the ended index serves
        // purge. Progress and its contract are stored together or not at all.
        return $"""
            SELECT pg_advisory_xact_lock(hashtextextended(@LockResource, 0));

            {PostgreSqlSchemaInitLock.AcquireStatement(schema)}
            CREATE SCHEMA IF NOT EXISTS "{schema}";

            CREATE SEQUENCE IF NOT EXISTS {sequence} AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;

            CREATE TABLE IF NOT EXISTS {table} (
                {PostgreSqlFencingSchema.TenantId} varchar({FencingFieldLimits.TenantIdMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlFencingSchema.Kind} varchar({FencingFieldLimits.KindMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlFencingSchema.Resource} varchar({FencingFieldLimits.ResourceMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlFencingSchema.Generation} bigint NOT NULL,
                {PostgreSqlFencingSchema.State} smallint NOT NULL,
                {PostgreSqlFencingSchema.GrantedAt} timestamptz NOT NULL,
                {PostgreSqlFencingSchema.ExpiresAt} timestamptz NOT NULL,
                {PostgreSqlFencingSchema.EndedAt} timestamptz NULL,
                {PostgreSqlFencingSchema.TakeoverCount} integer NOT NULL DEFAULT 0,
                {PostgreSqlFencingSchema.Progress} bytea NULL,
                {PostgreSqlFencingSchema.ProgressContract} varchar({FencingFieldLimits.ProgressContractMaxLength}) NULL,
                CONSTRAINT "pk_{PostgreSqlFencingSchema.TableName}" PRIMARY KEY (
                    {PostgreSqlFencingSchema.TenantId},
                    {PostgreSqlFencingSchema.Kind},
                    {PostgreSqlFencingSchema.Resource}
                ),
                CONSTRAINT "ck_{PostgreSqlFencingSchema.TableName}_generation" CHECK ({PostgreSqlFencingSchema.Generation} > 0),
                CONSTRAINT "ck_{PostgreSqlFencingSchema.TableName}_state" CHECK (
                    {PostgreSqlFencingSchema.State} BETWEEN {PostgreSqlFencingSchema.Active} AND {PostgreSqlFencingSchema.Abandoned}
                ),
                CONSTRAINT "ck_{PostgreSqlFencingSchema.TableName}_ended_at" CHECK (
                    ({PostgreSqlFencingSchema.State} = {PostgreSqlFencingSchema.Active}) = ({PostgreSqlFencingSchema.EndedAt} IS NULL)
                ),
                CONSTRAINT "ck_{PostgreSqlFencingSchema.TableName}_takeover_count" CHECK ({PostgreSqlFencingSchema.TakeoverCount} >= 0),
                CONSTRAINT "ck_{PostgreSqlFencingSchema.TableName}_progress" CHECK (
                    ({PostgreSqlFencingSchema.Progress} IS NULL) = ({PostgreSqlFencingSchema.ProgressContract} IS NULL)
                )
            );

            CREATE INDEX IF NOT EXISTS "ix_{PostgreSqlFencingSchema.TableName}_active_expiry"
                ON {table} (
                    {PostgreSqlFencingSchema.Kind},
                    {PostgreSqlFencingSchema.ExpiresAt},
                    {PostgreSqlFencingSchema.TenantId},
                    {PostgreSqlFencingSchema.Resource}
                )
                WHERE {PostgreSqlFencingSchema.State} = {PostgreSqlFencingSchema.Active};

            CREATE INDEX IF NOT EXISTS "ix_{PostgreSqlFencingSchema.TableName}_ended"
                ON {table} ({PostgreSqlFencingSchema.Kind}, {PostgreSqlFencingSchema.EndedAt})
                WHERE {PostgreSqlFencingSchema.State} <> {PostgreSqlFencingSchema.Active};
            """;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "PostgreSqlFencingSchemaRaceObserved",
        Level = LogLevel.Information,
        Message = "PostgreSQL fencing initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Retrying the DDL once in a fresh transaction."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
#pragma warning restore CA2100
