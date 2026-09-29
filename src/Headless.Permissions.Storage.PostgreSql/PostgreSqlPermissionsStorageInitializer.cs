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
        var grantsName = _GrantsName(options);
        var definitionsName = _DefinitionsName(options);
        var groupsName = _GroupsName(options);

        // Serialize concurrent-startup DDL across replicas with a transaction-scoped advisory
        // lock keyed on the schema. Auto-released on COMMIT/ROLLBACK; no explicit release.
        var lockResource = $"headless_permissions_init:{options.Schema}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            {PostgreSqlSchemaInitLock.AcquireStatement(options.Schema)}
            CREATE SCHEMA IF NOT EXISTS "{options.Schema}";

            CREATE TABLE IF NOT EXISTS {_Qualified(options.Schema, groupsName)} (
                "id" uuid NOT NULL,
                "name" character varying({PermissionGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "display_name" character varying({PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "extra_properties" text NOT NULL,
                CONSTRAINT "pk_{groupsName}" PRIMARY KEY ("id")
            );

            CREATE TABLE IF NOT EXISTS {_Qualified(options.Schema, definitionsName)} (
                "id" uuid NOT NULL,
                "group_name" character varying({PermissionGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "name" character varying({PermissionDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "display_name" character varying({PermissionDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "is_enabled" boolean NOT NULL,
                "parent_name" character varying({PermissionDefinitionRecordConstants.NameMaxLength}),
                "providers" character varying({PermissionDefinitionRecordConstants.ProvidersMaxLength}),
                "extra_properties" text NOT NULL,
                CONSTRAINT "pk_{definitionsName}" PRIMARY KEY ("id")
            );

            CREATE TABLE IF NOT EXISTS {_Qualified(options.Schema, grantsName)} (
                "id" uuid NOT NULL,
                "name" character varying({PermissionGrantRecordConstants.NameMaxLength}) NOT NULL,
                "provider_name" character varying({PermissionGrantRecordConstants.ProviderNameMaxLength}) NOT NULL,
                "provider_key" character varying({PermissionGrantRecordConstants.ProviderKeyMaxLength}) NOT NULL,
                "tenant_id" character varying({PermissionGrantRecordConstants.TenantIdMaxLength}),
                "is_granted" boolean NOT NULL DEFAULT TRUE,
                "created_at" timestamp with time zone NOT NULL,
                "updated_at" timestamp with time zone,
                CONSTRAINT "pk_{grantsName}" PRIMARY KEY ("id")
            );
            """;
    }

    private static string _CreateIndexesScript(PermissionsStorageOptions options)
    {
        var grantsName = _GrantsName(options);
        var definitionsName = _DefinitionsName(options);
        var groupsName = _GroupsName(options);
        var grantsTable = _Qualified(options.Schema, grantsName);
        var definitionsTable = _Qualified(options.Schema, definitionsName);
        var groupsTable = _Qualified(options.Schema, groupsName);

        var lockResource = $"headless_permissions_init:{options.Schema}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{groupsName}_name" ON {groupsTable} ("name");
            CREATE INDEX IF NOT EXISTS "ix_{definitionsName}_group_name" ON {definitionsTable} ("group_name");
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{definitionsName}_name" ON {definitionsTable} ("name");
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{grantsName}_tenant_id_name_provider_name_provider_key" ON {grantsTable} ("tenant_id", "name", "provider_name", "provider_key") WHERE "tenant_id" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{grantsName}_name_provider_name_provider_key_no_tenant" ON {grantsTable} ("name", "provider_name", "provider_key") WHERE "tenant_id" IS NULL;
            """;
    }

    /// <summary>Returns the qualified permission grants table.</summary>
    internal static string GrantsTable(PermissionsStorageOptions options)
    {
        return _Qualified(options.Schema, _GrantsName(options));
    }

    /// <summary>Returns the qualified permission definitions table.</summary>
    internal static string DefinitionsTable(PermissionsStorageOptions options)
    {
        return _Qualified(options.Schema, _DefinitionsName(options));
    }

    /// <summary>Returns the qualified permission group definitions table.</summary>
    internal static string GroupsTable(PermissionsStorageOptions options)
    {
        return _Qualified(options.Schema, _GroupsName(options));
    }

    private static string _GrantsName(PermissionsStorageOptions options)
    {
        return options.ResolvePermissionGrantsTableName(StorageNamingStyle.SnakeCase);
    }

    private static string _DefinitionsName(PermissionsStorageOptions options)
    {
        return options.ResolvePermissionDefinitionsTableName(StorageNamingStyle.SnakeCase);
    }

    private static string _GroupsName(PermissionsStorageOptions options)
    {
        return options.ResolvePermissionGroupDefinitionsTableName(StorageNamingStyle.SnakeCase);
    }

    // Quoted so a configured table name keeps its exact case; the default snake_case names read the same unquoted.
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
