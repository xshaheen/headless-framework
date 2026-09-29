// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.Hosting.Initialization;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.AuditLog.PostgreSql;

internal sealed partial class PostgreSqlAuditLogStorageInitializer(
    IOptions<PostgreSqlAuditLogOptions> providerOptions,
    IOptions<AuditLogStorageOptions> storageOptions,
    ILogger<PostgreSqlAuditLogStorageInitializer>? logger = null
) : HostedInitializer
{
    private readonly ILogger<PostgreSqlAuditLogStorageInitializer> _logger =
        logger ?? NullLogger<PostgreSqlAuditLogStorageInitializer>.Instance;

    protected override bool RunOnStartup => storageOptions.Value.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var options = storageOptions.Value;
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Split table-creation DDL from index-creation DDL into separate transactions, so a race absorbed on
        // one side rolls back and reruns only that side's batch.
        await _RunSchemaAndTableAsync(connection, options, cancellationToken).ConfigureAwait(false);
        await _RunIndexesAsync(connection, options, cancellationToken).ConfigureAwait(false);
    }

    private Task _RunSchemaAndTableAsync(
        NpgsqlConnection connection,
        AuditLogStorageOptions options,
        CancellationToken cancellationToken
    )
    {
        return _RunDdlAsync(connection, _CreateSchemaAndTableScript(options), cancellationToken);
    }

    private Task _RunIndexesAsync(
        NpgsqlConnection connection,
        AuditLogStorageOptions options,
        CancellationToken cancellationToken
    )
    {
        return _RunDdlAsync(connection, _CreateIndexesScript(options), cancellationToken);
    }

    // The advisory locks serialize our initializers, but a schema or object creator outside them (a consumer's EF
    // migration, other application code) can still commit the same CREATE first. That fails our transaction with
    // 42P06/42P07/42710, or 23505 on the catalog unique index when the two inserts race, and the rollback takes every
    // object of this batch with it. The conflicting creator has committed by the time we see the error, so one rerun
    // in a fresh transaction passes its IF NOT EXISTS guards and creates what the rollback discarded. A second failure
    // is not a race and propagates, so the initializer never reports success with its tables missing.
    private async Task _RunDdlAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                command.CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds;
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

    // Quoted so a configured table name keeps its exact case; the default snake_case name reads the same unquoted.
    internal static string Qualified(AuditLogStorageOptions options)
    {
        return $"""
            "{options.Schema}"."{TableName(options)}"
            """;
    }

    internal static string TableName(AuditLogStorageOptions options)
    {
        return options.ResolveTableName(StorageNamingStyle.SnakeCase);
    }

    private static string _IndexName(string tableName, string[] parts)
    {
        return HeadlessStorageNaming.IndexName(StorageNamingStyle.SnakeCase, tableName, parts);
    }

    private static string _CreateSchemaAndTableScript(AuditLogStorageOptions options)
    {
        var tableName = TableName(options);
        var table = Qualified(options);
        var primaryKey = HeadlessStorageNaming.PrimaryKeyName(StorageNamingStyle.SnakeCase, tableName);
        var createSchema = $"""
            {PostgreSqlSchemaInitLock.AcquireStatement(options.Schema)}
            CREATE SCHEMA IF NOT EXISTS "{options.Schema}";
            """;
        var jsonColumnType = (options.JsonColumnType ?? AuditLogJsonColumnType.Jsonb).ToSqlFragment();
        var createdAtColumnType = string.IsNullOrWhiteSpace(options.CreatedAtColumnType)
            ? "timestamp with time zone"
            : options.CreatedAtColumnType;

        // Serialize concurrent-startup DDL across replicas with a transaction-scoped advisory
        // lock keyed on (schema, table). Without this, racing CREATE SCHEMA IF NOT EXISTS calls
        // both attempt to insert into pg_namespace and one fails with 23505. The lock is
        // automatically released on COMMIT/ROLLBACK, no explicit release needed.
        var lockResource = $"headless_audit_init:{options.Schema}.{tableName}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            {createSchema}

            CREATE TABLE IF NOT EXISTS {table} (
                "id" bigint GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                "created_at" {createdAtColumnType} NOT NULL,
                "user_id" character varying({AuditLogFieldLimits.UserId}),
                "account_id" character varying({AuditLogFieldLimits.AccountId}),
                "tenant_id" character varying({AuditLogFieldLimits.TenantId}),
                "ip_address" character varying({AuditLogFieldLimits.IpAddress}),
                "user_agent" character varying({AuditLogFieldLimits.UserAgent}),
                "correlation_id" character varying({AuditLogFieldLimits.CorrelationId}),
                "action" character varying({AuditLogFieldLimits.Action}) NOT NULL,
                "change_type" integer,
                "entity_type" character varying({AuditLogFieldLimits.EntityType}),
                "entity_id" character varying({AuditLogFieldLimits.EntityId}),
                "old_values" {jsonColumnType},
                "new_values" {jsonColumnType},
                "changed_fields" {jsonColumnType},
                "success" boolean NOT NULL,
                "error_code" character varying({AuditLogFieldLimits.ErrorCode}),
                CONSTRAINT "{primaryKey}" PRIMARY KEY ("created_at", "id")
            );
            """;
    }

    private static string _CreateIndexesScript(AuditLogStorageOptions options)
    {
        var tableName = TableName(options);
        var table = Qualified(options);
        var tenantTime = _IndexName(tableName, AuditLogStorageNames.TenantTime);
        var tenantActionTime = _IndexName(tableName, AuditLogStorageNames.TenantActionTime);
        var tenantEntityTime = _IndexName(tableName, AuditLogStorageNames.TenantEntityTime);
        var tenantActorTime = _IndexName(tableName, AuditLogStorageNames.TenantActorTime);
        var tenantAccountTime = _IndexName(tableName, AuditLogStorageNames.TenantAccountTime);
        var correlation = _IndexName(tableName, AuditLogStorageNames.Correlation);

        // Re-acquire the advisory lock so multi-replica races on CREATE INDEX serialize the same
        // way as the table-create path. Released automatically on COMMIT/ROLLBACK.
        var lockResource = $"headless_audit_init:{options.Schema}.{tableName}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        // Each index ends in (created_at, id), the keyset order of read paging, so a filtered page seeks straight
        // to its continuation position instead of scanning.
        return $"""
            {acquireLock}

            CREATE INDEX IF NOT EXISTS "{tenantTime}" ON {table} ("tenant_id", "created_at", "id");
            CREATE INDEX IF NOT EXISTS "{tenantActionTime}" ON {table} ("tenant_id", "action", "created_at", "id");
            CREATE INDEX IF NOT EXISTS "{tenantEntityTime}" ON {table} ("tenant_id", "entity_type", "entity_id", "created_at", "id");
            CREATE INDEX IF NOT EXISTS "{tenantActorTime}" ON {table} ("tenant_id", "user_id", "created_at", "id");
            CREATE INDEX IF NOT EXISTS "{tenantAccountTime}" ON {table} ("tenant_id", "account_id", "created_at", "id");
            CREATE INDEX IF NOT EXISTS "{correlation}" ON {table} ("correlation_id", "created_at", "id");
            """;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "PostgreSqlAuditLogSchemaRaceObserved",
        Level = LogLevel.Information,
        Message = "PostgreSql audit-log initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Retrying the DDL once in a fresh transaction."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
