// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
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

        // Split table-creation DDL from index-creation DDL into separate transactions. If a racing
        // initializer trips 42P07/42710 on the table side, the rollback that follows must not also
        // wipe the index DDL — those indexes would be skipped on the IsInitialized=true path and the
        // tables would live without their tenant-time covering indexes until a manual repair.
        await _RunSchemaAndTableAsync(connection, options, cancellationToken).ConfigureAwait(false);
        await _RunIndexesAsync(connection, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task _RunSchemaAndTableAsync(
        NpgsqlConnection connection,
        AuditLogStorageOptions options,
        CancellationToken cancellationToken
    )
    {
        var sql = _CreateSchemaAndTableScript(options);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        // 42P06: schema_already_exists, 42P07: relation_already_exists (table/index),
        // 42710: duplicate_object, 23505: unique_violation on pg_namespace_nspname_index when
        // two transactions race CREATE SCHEMA IF NOT EXISTS (the IF NOT EXISTS check is not
        // transactional with the catalog insert). The pg_advisory_xact_lock serializes ours, but
        // a foreign initializer running concurrent DDL can still trigger this path — absorb it
        // and treat the schema as initialized. Index creation runs in a separate transaction
        // below so it isn't wiped by this rollback.
        catch (PostgresException ex) when (ex.SqlState is "42P06" or "42P07" or "42710" or "23505")
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            LogSchemaRaceObserved(_logger, ex.SqlState, ex.MessageText);
        }
    }

    private async Task _RunIndexesAsync(
        NpgsqlConnection connection,
        AuditLogStorageOptions options,
        CancellationToken cancellationToken
    )
    {
        var sql = _CreateIndexesScript(options);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        // Each CREATE INDEX uses IF NOT EXISTS so re-runs are idempotent; absorb any racing
        // duplicate-object state codes from a foreign initializer running concurrent DDL.
        catch (PostgresException ex) when (ex.SqlState is "42P07" or "42710")
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            LogSchemaRaceObserved(_logger, ex.SqlState, ex.MessageText);
        }
    }

    internal static string Qualified(AuditLogStorageOptions options)
    {
        return $"""
            "{options.Schema}"."{options.TableName}"
            """;
    }

    private static string _CreateSchemaAndTableScript(AuditLogStorageOptions options)
    {
        var table = Qualified(options);
        var createSchema = $"""CREATE SCHEMA IF NOT EXISTS "{options.Schema}";""";
        var jsonColumnType = (options.JsonColumnType ?? AuditLogJsonColumnType.Jsonb).ToSqlFragment();
        var createdAtColumnType = string.IsNullOrWhiteSpace(options.CreatedAtColumnType)
            ? "timestamp with time zone"
            : options.CreatedAtColumnType;

        // Serialize concurrent-startup DDL across replicas with a transaction-scoped advisory
        // lock keyed on (schema, table). Without this, racing CREATE SCHEMA IF NOT EXISTS calls
        // both attempt to insert into pg_namespace and one fails with 23505. The lock is
        // automatically released on COMMIT/ROLLBACK, no explicit release needed.
        var lockResource = $"headless_audit_init:{options.Schema}.{options.TableName}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            {createSchema}

            CREATE TABLE IF NOT EXISTS {table} (
                "Id" bigint GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                "CreatedAt" {createdAtColumnType} NOT NULL,
                "UserId" character varying({AuditLogFieldLimits.UserId}),
                "AccountId" character varying({AuditLogFieldLimits.AccountId}),
                "TenantId" character varying({AuditLogFieldLimits.TenantId}),
                "IpAddress" character varying({AuditLogFieldLimits.IpAddress}),
                "UserAgent" character varying({AuditLogFieldLimits.UserAgent}),
                "CorrelationId" character varying({AuditLogFieldLimits.CorrelationId}),
                "Action" character varying({AuditLogFieldLimits.Action}) NOT NULL,
                "ChangeType" integer,
                "EntityType" character varying({AuditLogFieldLimits.EntityType}),
                "EntityId" character varying({AuditLogFieldLimits.EntityId}),
                "OldValues" {jsonColumnType},
                "NewValues" {jsonColumnType},
                "ChangedFields" {jsonColumnType},
                "Success" boolean NOT NULL,
                "ErrorCode" character varying({AuditLogFieldLimits.ErrorCode}),
                CONSTRAINT "PK_{options.TableName}" PRIMARY KEY ("CreatedAt", "Id")
            );
            """;
    }

    private static string _CreateIndexesScript(AuditLogStorageOptions options)
    {
        var table = Qualified(options);

        // Re-acquire the advisory lock so multi-replica races on CREATE INDEX serialize the same
        // way as the table-create path. Released automatically on COMMIT/ROLLBACK.
        var lockResource = $"headless_audit_init:{options.Schema}.{options.TableName}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            CREATE INDEX IF NOT EXISTS "ix_audit_log_tenant_time" ON {table} ("TenantId", "CreatedAt");
            CREATE INDEX IF NOT EXISTS "ix_audit_log_tenant_action_time" ON {table} ("TenantId", "Action", "CreatedAt");
            CREATE INDEX IF NOT EXISTS "ix_audit_log_tenant_entity_time" ON {table} ("TenantId", "EntityType", "EntityId", "CreatedAt");
            CREATE INDEX IF NOT EXISTS "ix_audit_log_tenant_actor_time" ON {table} ("TenantId", "UserId", "CreatedAt");
            CREATE INDEX IF NOT EXISTS "ix_audit_log_correlation" ON {table} ("CorrelationId");
            """;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "PostgreSqlAuditLogSchemaRaceObserved",
        Level = LogLevel.Information,
        Message = "PostgreSql audit-log initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Treating schema as initialized."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
