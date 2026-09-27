// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.Hosting.Initialization;
using Headless.Permissions.Entities;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Permissions.PostgreSql;

internal sealed partial class PostgreSqlPermissionsStorageInitializer(
    IOptions<PostgreSqlPermissionsOptions> providerOptions,
    IOptions<PermissionsStorageOptions> storageOptions,
    ILogger<PostgreSqlPermissionsStorageInitializer>? logger = null
) : HostedInitializer
{
    private readonly ILogger<PostgreSqlPermissionsStorageInitializer> _logger =
        logger ?? NullLogger<PostgreSqlPermissionsStorageInitializer>.Instance;

    protected override bool RunOnStartup => storageOptions.Value.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var options = storageOptions.Value;
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Split table-creation DDL from index-creation DDL into separate transactions, so a race absorbed on
        // one side rolls back and reruns only that side's batch.
        await _RunSchemaAndTablesAsync(connection, options, cancellationToken).ConfigureAwait(false);
        await _RunIndexesAsync(connection, options, cancellationToken).ConfigureAwait(false);
    }

    private Task _RunSchemaAndTablesAsync(
        NpgsqlConnection connection,
        PermissionsStorageOptions options,
        CancellationToken cancellationToken
    )
    {
        return _RunDdlAsync(connection, _CreateSchemaAndTablesScript(options), cancellationToken);
    }

    private Task _RunIndexesAsync(
        NpgsqlConnection connection,
        PermissionsStorageOptions options,
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

    private static string _CreateSchemaAndTablesScript(PermissionsStorageOptions options)
    {
        var grantsTable = _Qualified(options.Schema, options.PermissionGrantsTableName);
        var definitionsTable = _Qualified(options.Schema, options.PermissionDefinitionsTableName);
        var groupsTable = _Qualified(options.Schema, options.PermissionGroupDefinitionsTableName);

        // Serialize concurrent-startup DDL across replicas with a transaction-scoped advisory
        // lock keyed on the schema. Auto-released on COMMIT/ROLLBACK; no explicit release.
        var lockResource = $"headless_permissions_init:{options.Schema}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            {PostgreSqlSchemaInitLock.AcquireStatement(options.Schema)}
            CREATE SCHEMA IF NOT EXISTS "{options.Schema}";

            CREATE TABLE IF NOT EXISTS {groupsTable} (
                "Id" uuid NOT NULL,
                "Name" character varying({PermissionGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "DisplayName" character varying({PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_{options.PermissionGroupDefinitionsTableName}" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS {definitionsTable} (
                "Id" uuid NOT NULL,
                "GroupName" character varying({PermissionGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "Name" character varying({PermissionDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "DisplayName" character varying({PermissionDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "IsEnabled" boolean NOT NULL,
                "ParentName" character varying({PermissionDefinitionRecordConstants.NameMaxLength}),
                "Providers" character varying({PermissionDefinitionRecordConstants.ProvidersMaxLength}),
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_{options.PermissionDefinitionsTableName}" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS {grantsTable} (
                "Id" uuid NOT NULL,
                "Name" character varying({PermissionGrantRecordConstants.NameMaxLength}) NOT NULL,
                "ProviderName" character varying({PermissionGrantRecordConstants.ProviderNameMaxLength}) NOT NULL,
                "ProviderKey" character varying({PermissionGrantRecordConstants.ProviderKeyMaxLength}) NOT NULL,
                "TenantId" character varying({PermissionGrantRecordConstants.TenantIdMaxLength}),
                "IsGranted" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone,
                CONSTRAINT "PK_{options.PermissionGrantsTableName}" PRIMARY KEY ("Id")
            );

            DO $migration$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = '{options.Schema}' AND table_name = '{options.PermissionGrantsTableName}' AND column_name = 'DateCreated'
                ) AND NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = '{options.Schema}' AND table_name = '{options.PermissionGrantsTableName}' AND column_name = 'CreatedAt'
                ) THEN
                    ALTER TABLE {grantsTable} RENAME COLUMN "DateCreated" TO "CreatedAt";
                END IF;

                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = '{options.Schema}' AND table_name = '{options.PermissionGrantsTableName}' AND column_name = 'DateUpdated'
                ) AND NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = '{options.Schema}' AND table_name = '{options.PermissionGrantsTableName}' AND column_name = 'UpdatedAt'
                ) THEN
                    ALTER TABLE {grantsTable} RENAME COLUMN "DateUpdated" TO "UpdatedAt";
                END IF;
            END $migration$;
            """;
    }

    private static string _CreateIndexesScript(PermissionsStorageOptions options)
    {
        var grantsTable = _Qualified(options.Schema, options.PermissionGrantsTableName);
        var definitionsTable = _Qualified(options.Schema, options.PermissionDefinitionsTableName);
        var groupsTable = _Qualified(options.Schema, options.PermissionGroupDefinitionsTableName);

        var lockResource = $"headless_permissions_init:{options.Schema}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.PermissionGroupDefinitionsTableName}_Name" ON {groupsTable} ("Name");
            CREATE INDEX IF NOT EXISTS "IX_{options.PermissionDefinitionsTableName}_GroupName" ON {definitionsTable} ("GroupName");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.PermissionDefinitionsTableName}_Name" ON {definitionsTable} ("Name");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.PermissionGrantsTableName}_TenantId_Name_ProviderName_ProviderKey" ON {grantsTable} ("TenantId", "Name", "ProviderName", "ProviderKey") WHERE "TenantId" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.PermissionGrantsTableName}_Name_ProviderName_ProviderKey_NullTenantId" ON {grantsTable} ("Name", "ProviderName", "ProviderKey") WHERE "TenantId" IS NULL;
            """;
    }

    internal static string Qualified(PermissionsStorageOptions options, string tableName)
    {
        return _Qualified(options.Schema, tableName);
    }

    private static string _Qualified(string schema, string tableName)
    {
        return $"""
            "{schema}"."{tableName}"
            """;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "PostgreSqlPermissionsSchemaRaceObserved",
        Level = LogLevel.Information,
        Message = "PostgreSql permissions initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Retrying the DDL once in a fresh transaction."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
