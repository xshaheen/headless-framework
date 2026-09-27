// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.Hosting.Initialization;
using Headless.Settings.Entities;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Settings.PostgreSql;

/// <summary>
/// Hosted initializer that creates or migrates the PostgreSQL schema and tables required by the
/// settings storage provider. Runs on startup when <see cref="SettingsStorageOptions.InitializeOnStartup"/> is
/// <see langword="true"/>. Concurrent startup races are absorbed via PostgreSQL advisory locks and
/// <c>IF NOT EXISTS</c> guards.
/// </summary>
internal sealed partial class PostgreSqlSettingsStorageInitializer(
    IOptions<PostgreSqlSettingsOptions> providerOptions,
    IOptions<SettingsStorageOptions> storageOptions,
    ILogger<PostgreSqlSettingsStorageInitializer>? logger = null
) : HostedInitializer
{
    private readonly ILogger<PostgreSqlSettingsStorageInitializer> _logger =
        logger ?? NullLogger<PostgreSqlSettingsStorageInitializer>.Instance;

    /// <summary>Gets a value indicating whether this initializer should run when the host starts.</summary>
    protected override bool RunOnStartup => storageOptions.Value.InitializeOnStartup;

    /// <summary>Creates the settings schema, tables, and indexes if they do not already exist.</summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
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

    /// <summary>Runs the DDL transaction that creates the schema and both tables.</summary>
    private Task _RunSchemaAndTablesAsync(
        NpgsqlConnection connection,
        SettingsStorageOptions options,
        CancellationToken cancellationToken
    )
    {
        return _RunDdlAsync(connection, _CreateSchemaAndTablesScript(options), cancellationToken);
    }

    /// <summary>Runs the DDL transaction that creates the unique indexes on the settings tables.</summary>
    private Task _RunIndexesAsync(
        NpgsqlConnection connection,
        SettingsStorageOptions options,
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

    /// <summary>Builds the SQL script that creates the schema and both settings tables using <c>IF NOT EXISTS</c> guards and an advisory lock.</summary>
    private static string _CreateSchemaAndTablesScript(SettingsStorageOptions options)
    {
        var valuesTable = _Qualified(options.Schema, options.SettingValuesTableName);
        var definitionsTable = _Qualified(options.Schema, options.SettingDefinitionsTableName);

        // Serialize concurrent-startup DDL across replicas with a transaction-scoped advisory
        // lock keyed on the schema (two tables share the schema, so a per-table key would still
        // race on CREATE SCHEMA). Auto-released on COMMIT/ROLLBACK; no explicit release needed.
        var lockResource = $"headless_settings_init:{options.Schema}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            {PostgreSqlSchemaInitLock.AcquireStatement(options.Schema)}
            CREATE SCHEMA IF NOT EXISTS "{options.Schema}";

            CREATE TABLE IF NOT EXISTS {definitionsTable} (
                "Id" uuid NOT NULL,
                "Name" character varying({SettingDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "DisplayName" character varying({SettingDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "Description" character varying({SettingDefinitionRecordConstants.DescriptionMaxLength}),
                "DefaultValue" character varying({SettingDefinitionRecordConstants.DefaultValueMaxLength}),
                "IsVisibleToClients" boolean NOT NULL,
                "IsInherited" boolean NOT NULL,
                "IsEncrypted" boolean NOT NULL,
                "Providers" character varying({SettingDefinitionRecordConstants.ProvidersMaxLength}),
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_{options.SettingDefinitionsTableName}" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS {valuesTable} (
                "Id" uuid NOT NULL,
                "Name" character varying({SettingValueRecordConstants.NameMaxLength}) NOT NULL,
                "Value" character varying({SettingValueRecordConstants.ValueMaxLength}) NOT NULL,
                "ProviderName" character varying({SettingValueRecordConstants.ProviderNameMaxLength}) NOT NULL,
                "ProviderKey" character varying({SettingValueRecordConstants.ProviderKeyMaxLength}),
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone,
                CONSTRAINT "PK_{options.SettingValuesTableName}" PRIMARY KEY ("Id")
            );

            DO $migration$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = '{options.Schema}' AND table_name = '{options.SettingValuesTableName}' AND column_name = 'DateCreated'
                ) AND NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = '{options.Schema}' AND table_name = '{options.SettingValuesTableName}' AND column_name = 'CreatedAt'
                ) THEN
                    ALTER TABLE {valuesTable} RENAME COLUMN "DateCreated" TO "CreatedAt";
                END IF;

                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = '{options.Schema}' AND table_name = '{options.SettingValuesTableName}' AND column_name = 'DateUpdated'
                ) AND NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = '{options.Schema}' AND table_name = '{options.SettingValuesTableName}' AND column_name = 'UpdatedAt'
                ) THEN
                    ALTER TABLE {valuesTable} RENAME COLUMN "DateUpdated" TO "UpdatedAt";
                END IF;
            END $migration$;
            """;
    }

    /// <summary>Builds the SQL script that creates the unique indexes on both settings tables using <c>IF NOT EXISTS</c> guards and an advisory lock.</summary>
    private static string _CreateIndexesScript(SettingsStorageOptions options)
    {
        var valuesTable = _Qualified(options.Schema, options.SettingValuesTableName);
        var definitionsTable = _Qualified(options.Schema, options.SettingDefinitionsTableName);

        var lockResource = $"headless_settings_init:{options.Schema}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.SettingDefinitionsTableName}_Name" ON {definitionsTable} ("Name");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.SettingValuesTableName}_Name_ProviderName_ProviderKey" ON {valuesTable} ("Name", "ProviderName", "ProviderKey") WHERE "ProviderKey" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.SettingValuesTableName}_Name_ProviderName_NullProviderKey" ON {valuesTable} ("Name", "ProviderName") WHERE "ProviderKey" IS NULL;
            """;
    }

    /// <summary>Returns the fully-qualified, double-quoted <c>"schema"."table"</c> identifier for <paramref name="tableName"/>.</summary>
    /// <param name="options">Storage options that supply the schema name.</param>
    /// <param name="tableName">Unqualified table name.</param>
    /// <returns>A double-quoted, schema-qualified table reference safe for interpolation into SQL.</returns>
    internal static string Qualified(SettingsStorageOptions options, string tableName)
    {
        return _Qualified(options.Schema, tableName);
    }

    /// <summary>Returns <c>"<paramref name="schema"/>"."<paramref name="tableName"/>"</c>.</summary>
    private static string _Qualified(string schema, string tableName)
    {
        return $"""
            "{schema}"."{tableName}"
            """;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "PostgreSqlSettingsSchemaRaceObserved",
        Level = LogLevel.Information,
        Message = "PostgreSql settings initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Retrying the DDL once in a fresh transaction."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
