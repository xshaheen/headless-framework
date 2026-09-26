// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.Hosting.Initialization;
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
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

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
        }
        // The advisory lock serializes this initializer, but IF NOT EXISTS is not transactional with the catalog
        // insert, so a foreign process running the same DDL concurrently can still surface a duplicate. The object
        // exists either way, so the race is absorbed and the schema treated as ready.
        catch (PostgresException ex)
            when (ex.SqlState
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

    private static string _CreateScript(string schema)
    {
        var table = PostgreSqlFencingSchema.QualifiedTable(schema);
        var sequence = PostgreSqlFencingSchema.QualifiedSequence(schema);

        // Key columns compare with the "C" collation, so kinds, resources, and tenant ids match ordinally (byte for
        // byte) whatever the database's default collation is. One store-wide sequence issues every generation, so a
        // lease granted again after its row was purged still gets a generation above every earlier one. The active
        // index serves the sweep's keyset walk in (expires_at, tenant_id, resource) order; the ended index serves
        // purge.
        return $"""
            SELECT pg_advisory_xact_lock(hashtextextended(@LockResource, 0));

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
        Message = "PostgreSQL fencing initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Treating the schema as initialized."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
#pragma warning restore CA2100
