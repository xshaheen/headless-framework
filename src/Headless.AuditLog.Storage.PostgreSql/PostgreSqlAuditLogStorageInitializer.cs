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

    internal static string Qualified(AuditLogStorageOptions options)
    {
        return $"""
            "{options.Schema}"."{options.TableName}"
            """;
    }

    private static string _CreateSchemaAndTableScript(AuditLogStorageOptions options)
    {
        var table = Qualified(options);
        var primaryKey = AuditLogStorageNames.PrimaryKey(options.TableName);
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
                CONSTRAINT "{primaryKey}" PRIMARY KEY ("CreatedAt", "Id")
            );
            """;
    }

    private static string _CreateIndexesScript(AuditLogStorageOptions options)
    {
        var table = Qualified(options);
        var tenantTime = AuditLogStorageNames.TenantTimeIndex(options.TableName);
        var tenantActionTime = AuditLogStorageNames.TenantActionTimeIndex(options.TableName);
        var tenantEntityTime = AuditLogStorageNames.TenantEntityTimeIndex(options.TableName);
        var tenantActorTime = AuditLogStorageNames.TenantActorTimeIndex(options.TableName);
        var tenantAccountTime = AuditLogStorageNames.TenantAccountTimeIndex(options.TableName);
        var correlation = AuditLogStorageNames.CorrelationIndex(options.TableName);

        // Re-acquire the advisory lock so multi-replica races on CREATE INDEX serialize the same
        // way as the table-create path. Released automatically on COMMIT/ROLLBACK.
        var lockResource = $"headless_audit_init:{options.Schema}.{options.TableName}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        // Each index ends in (CreatedAt, Id), the keyset order of read paging, so a filtered page seeks straight
        // to its continuation position instead of scanning.
        return $"""
            {acquireLock}

            CREATE INDEX IF NOT EXISTS "{tenantTime}" ON {table} ("TenantId", "CreatedAt", "Id");
            CREATE INDEX IF NOT EXISTS "{tenantActionTime}" ON {table} ("TenantId", "Action", "CreatedAt", "Id");
            CREATE INDEX IF NOT EXISTS "{tenantEntityTime}" ON {table} ("TenantId", "EntityType", "EntityId", "CreatedAt", "Id");
            CREATE INDEX IF NOT EXISTS "{tenantActorTime}" ON {table} ("TenantId", "UserId", "CreatedAt", "Id");
            CREATE INDEX IF NOT EXISTS "{tenantAccountTime}" ON {table} ("TenantId", "AccountId", "CreatedAt", "Id");
            CREATE INDEX IF NOT EXISTS "{correlation}" ON {table} ("CorrelationId", "CreatedAt", "Id");
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
