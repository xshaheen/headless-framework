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
        var valuesName = _ValuesName(options);
        var definitionsName = _DefinitionsName(options);

        // Serialize concurrent-startup DDL across replicas with a transaction-scoped advisory
        // lock keyed on the schema (two tables share the schema, so a per-table key would still
        // race on CREATE SCHEMA). Auto-released on COMMIT/ROLLBACK; no explicit release needed.
        var lockResource = $"headless_settings_init:{options.Schema}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            {PostgreSqlSchemaInitLock.AcquireStatement(options.Schema)}
            CREATE SCHEMA IF NOT EXISTS "{options.Schema}";

            CREATE TABLE IF NOT EXISTS {_Qualified(options.Schema, definitionsName)} (
                "id" uuid NOT NULL,
                "name" character varying({SettingDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "display_name" character varying({SettingDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "description" character varying({SettingDefinitionRecordConstants.DescriptionMaxLength}),
                "default_value" character varying({SettingDefinitionRecordConstants.DefaultValueMaxLength}),
                "is_visible_to_clients" boolean NOT NULL,
                "is_inherited" boolean NOT NULL,
                "is_encrypted" boolean NOT NULL,
                "providers" character varying({SettingDefinitionRecordConstants.ProvidersMaxLength}),
                "extra_properties" text NOT NULL,
                CONSTRAINT "pk_{definitionsName}" PRIMARY KEY ("id")
            );

            CREATE TABLE IF NOT EXISTS {_Qualified(options.Schema, valuesName)} (
                "id" uuid NOT NULL,
                "name" character varying({SettingValueRecordConstants.NameMaxLength}) NOT NULL,
                "value" character varying({SettingValueRecordConstants.ValueMaxLength}) NOT NULL,
                "provider_name" character varying({SettingValueRecordConstants.ProviderNameMaxLength}) NOT NULL,
                "provider_key" character varying({SettingValueRecordConstants.ProviderKeyMaxLength}),
                "created_at" timestamp with time zone NOT NULL,
                "updated_at" timestamp with time zone,
                CONSTRAINT "pk_{valuesName}" PRIMARY KEY ("id")
            );
            """;
    }

    /// <summary>Builds the SQL script that creates the unique indexes on both settings tables using <c>IF NOT EXISTS</c> guards and an advisory lock.</summary>
    private static string _CreateIndexesScript(SettingsStorageOptions options)
    {
        var valuesName = _ValuesName(options);
        var definitionsName = _DefinitionsName(options);
        var valuesTable = _Qualified(options.Schema, valuesName);
        var definitionsTable = _Qualified(options.Schema, definitionsName);

        var lockResource = $"headless_settings_init:{options.Schema}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{definitionsName}_name" ON {definitionsTable} ("name");
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{valuesName}_name_provider_name_provider_key" ON {valuesTable} ("name", "provider_name", "provider_key") WHERE "provider_key" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{valuesName}_name_provider_name_null_provider_key" ON {valuesTable} ("name", "provider_name") WHERE "provider_key" IS NULL;
            """;
    }

    /// <summary>Returns the qualified setting values table.</summary>
    internal static string ValuesTable(SettingsStorageOptions options)
    {
        return _Qualified(options.Schema, _ValuesName(options));
    }

    /// <summary>Returns the qualified setting definitions table.</summary>
    internal static string DefinitionsTable(SettingsStorageOptions options)
    {
        return _Qualified(options.Schema, _DefinitionsName(options));
    }

    private static string _ValuesName(SettingsStorageOptions options)
    {
        return options.ResolveSettingValuesTableName(StorageNamingStyle.SnakeCase);
    }

    private static string _DefinitionsName(SettingsStorageOptions options)
    {
        return options.ResolveSettingDefinitionsTableName(StorageNamingStyle.SnakeCase);
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
        EventName = "PostgreSqlSettingsSchemaRaceObserved",
        Level = LogLevel.Information,
        Message = "PostgreSql settings initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Retrying the DDL once in a fresh transaction."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
