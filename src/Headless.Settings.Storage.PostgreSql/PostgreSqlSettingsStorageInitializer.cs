// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Settings.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Settings.PostgreSql;

internal sealed partial class PostgreSqlSettingsStorageInitializer(
    IOptions<PostgreSqlSettingsOptions> providerOptions,
    IOptions<SettingsStorageOptions> storageOptions,
    ILogger<PostgreSqlSettingsStorageInitializer>? logger = null
) : StorageInitializerBase
{
    private readonly ILogger<PostgreSqlSettingsStorageInitializer> _logger =
        logger ?? NullLogger<PostgreSqlSettingsStorageInitializer>.Instance;

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
        SettingsStorageOptions options,
        CancellationToken cancellationToken
    )
    {
        var sql = _CreateSchemaAndTablesScript(options);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction)
            {
                CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds,
            };
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState is "42P06" or "42P07" or "42710" or "23505")
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            LogSchemaRaceObserved(_logger, ex.SqlState, ex.MessageText);
        }
    }

    private async Task _RunIndexesAsync(
        NpgsqlConnection connection,
        SettingsStorageOptions options,
        CancellationToken cancellationToken
    )
    {
        var sql = _CreateIndexesScript(options);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction)
            {
                CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds,
            };
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState is "42P07" or "42710")
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            LogSchemaRaceObserved(_logger, ex.SqlState, ex.MessageText);
        }
    }

    private static string _CreateSchemaAndTablesScript(SettingsStorageOptions options)
    {
        var valuesTable = _Qualified(options.Schema, options.SettingValuesTableName);
        var definitionsTable = _Qualified(options.Schema, options.SettingDefinitionsTableName);

        // Serialize concurrent-startup DDL across replicas with a transaction-scoped advisory
        // lock keyed on the schema (two tables share the schema, so a per-table key would still
        // race on CREATE SCHEMA). Auto-released on COMMIT/ROLLBACK; no explicit release needed.
        var lockResource = $"headless_settings_init:{options.Schema}";
        var acquireLock = $"""SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));""";

        return $"""
            {acquireLock}

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
                "DateCreated" timestamp with time zone NOT NULL,
                "DateUpdated" timestamp with time zone,
                CONSTRAINT "PK_{options.SettingValuesTableName}" PRIMARY KEY ("Id")
            );
            """;
    }

    private static string _CreateIndexesScript(SettingsStorageOptions options)
    {
        var valuesTable = _Qualified(options.Schema, options.SettingValuesTableName);
        var definitionsTable = _Qualified(options.Schema, options.SettingDefinitionsTableName);

        var lockResource = $"headless_settings_init:{options.Schema}";
        var acquireLock = $"""SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));""";

        return $"""
            {acquireLock}

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.SettingDefinitionsTableName}_Name" ON {definitionsTable} ("Name");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.SettingValuesTableName}_Name_ProviderName_ProviderKey" ON {valuesTable} ("Name", "ProviderName", "ProviderKey") WHERE "ProviderKey" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.SettingValuesTableName}_Name_ProviderName_NullProviderKey" ON {valuesTable} ("Name", "ProviderName") WHERE "ProviderKey" IS NULL;
            """;
    }

    internal static string Qualified(SettingsStorageOptions options, string tableName) => _Qualified(options.Schema, tableName);

    private static string _Qualified(string schema, string tableName) => $@"""{schema}"".""{tableName}""";

    [LoggerMessage(
        EventId = 1,
        EventName = "PostgreSqlSettingsSchemaRaceObserved",
        Level = LogLevel.Information,
        Message = "PostgreSql settings initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Treating schema as initialized."
    )]
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
