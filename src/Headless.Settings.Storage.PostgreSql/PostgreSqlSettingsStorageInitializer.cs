// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Settings.Entities;
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

        // Split table-creation DDL from index-creation DDL into separate transactions. If a racing
        // initializer trips 42P07/42710 on the table side, the rollback that follows must not also
        // wipe the index DDL — those indexes would be skipped on the IsInitialized=true path and the
        // tables would live without their unique covering indexes until a manual repair.
        await _RunSchemaAndTablesAsync(connection, options, cancellationToken).ConfigureAwait(false);
        await _RunIndexesAsync(connection, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the DDL transaction that creates the schema and both tables.
    /// A <c>42P06</c>, <c>42P07</c>, <c>42710</c>, or <c>23505</c> error from a concurrent initializer
    /// is swallowed and logged; any other error propagates.
    /// </summary>
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
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState is "42P06" or "42P07" or "42710" or "23505")
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            LogSchemaRaceObserved(_logger, ex.SqlState, ex.MessageText);
        }
    }

    /// <summary>
    /// Runs the DDL transaction that creates the unique indexes on the settings tables.
    /// A <c>42P07</c> or <c>42710</c> error from a concurrent initializer is swallowed and logged;
    /// any other error propagates.
    /// </summary>
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
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState is "42P07" or "42710")
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            LogSchemaRaceObserved(_logger, ex.SqlState, ex.MessageText);
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
    internal static string Qualified(SettingsStorageOptions options, string tableName) =>
        _Qualified(options.Schema, tableName);

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
        Message = "PostgreSql settings initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Treating schema as initialized."
    )]
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
