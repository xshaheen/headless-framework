// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.Hosting.Initialization;
using Headless.Permissions.Entities;
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

        // Split table-creation DDL from index-creation DDL into separate transactions. If a racing
        // initializer trips 42P07/42710 on the table side, the rollback that follows must not also
        // wipe the index DDL — those indexes would be skipped on the IsInitialized=true path and the
        // tables would live without their unique covering indexes until a manual repair.
        await _RunSchemaAndTablesAsync(connection, options, cancellationToken).ConfigureAwait(false);
        await _RunIndexesAsync(connection, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task _RunSchemaAndTablesAsync(
        NpgsqlConnection connection,
        PermissionsStorageOptions options,
        CancellationToken cancellationToken
    )
    {
        var sql = _CreateSchemaAndTablesScript(options);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
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

    private async Task _RunIndexesAsync(
        NpgsqlConnection connection,
        PermissionsStorageOptions options,
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
        catch (PostgresException ex)
            when (ex.SqlState is SqlErrorCodes.PostgreSql.DuplicateTable or SqlErrorCodes.PostgreSql.DuplicateObject)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            LogSchemaRaceObserved(_logger, ex.SqlState, ex.MessageText);
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
        Message = "PostgreSql permissions initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Treating schema as initialized."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
