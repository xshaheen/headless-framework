// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Headless.Hosting.Initialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Features.PostgreSql;

/// <summary>
/// Hosted initializer that creates or ensures the PostgreSQL schema, tables, and indexes
/// required by the features storage provider. Runs on startup when
/// <see cref="FeaturesStorageOptions.InitializeOnStartup"/> is <see langword="true"/>.
/// </summary>
internal sealed partial class PostgreSqlFeaturesStorageInitializer(
    IOptions<PostgreSqlFeaturesOptions> providerOptions,
    IOptions<FeaturesStorageOptions> storageOptions,
    ILogger<PostgreSqlFeaturesStorageInitializer>? logger = null
) : HostedInitializer
{
    private readonly ILogger<PostgreSqlFeaturesStorageInitializer> _logger =
        logger ?? NullLogger<PostgreSqlFeaturesStorageInitializer>.Instance;

    /// <inheritdoc/>
    protected override bool RunOnStartup => storageOptions.Value.InitializeOnStartup;

    /// <summary>
    /// Creates or ensures the PostgreSQL schema, tables, and indexes required by the features
    /// storage provider. DDL is split into two transactions — one for schema/tables and one for
    /// indexes — so that a concurrent-DDL race on either side does not prevent the other from
    /// completing.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
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
        FeaturesStorageOptions options,
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

    private async Task _RunIndexesAsync(
        NpgsqlConnection connection,
        FeaturesStorageOptions options,
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

    private static string _CreateSchemaAndTablesScript(FeaturesStorageOptions options)
    {
        var valuesTable = _Qualified(options.Schema, options.FeatureValuesTableName);
        var definitionsTable = _Qualified(options.Schema, options.FeatureDefinitionsTableName);
        var groupsTable = _Qualified(options.Schema, options.FeatureGroupDefinitionsTableName);

        // Serialize concurrent-startup DDL across replicas with a transaction-scoped advisory
        // lock keyed on the schema (multiple tables share the schema). Auto-released on COMMIT/ROLLBACK.
        var lockResource = $"headless_features_init:{options.Schema}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            CREATE SCHEMA IF NOT EXISTS "{options.Schema}";

            CREATE TABLE IF NOT EXISTS {groupsTable} (
                "Id" uuid NOT NULL,
                "Name" character varying({FeatureGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "DisplayName" character varying({FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_{options.FeatureGroupDefinitionsTableName}" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS {definitionsTable} (
                "Id" uuid NOT NULL,
                "GroupName" character varying({FeatureGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "Name" character varying({FeatureDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "DisplayName" character varying({FeatureDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "ParentName" character varying({FeatureDefinitionRecordConstants.NameMaxLength}),
                "Description" character varying({FeatureDefinitionRecordConstants.DescriptionMaxLength}),
                "DefaultValue" character varying({FeatureDefinitionRecordConstants.DefaultValueMaxLength}),
                "IsVisibleToClients" boolean NOT NULL,
                "IsAvailableToHost" boolean NOT NULL,
                "Providers" character varying({FeatureDefinitionRecordConstants.ProvidersMaxLength}),
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_{options.FeatureDefinitionsTableName}" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS {valuesTable} (
                "Id" uuid NOT NULL,
                "Name" character varying({FeatureValueRecordConstants.NameMaxLength}) NOT NULL,
                "Value" character varying({FeatureValueRecordConstants.ValueMaxLength}) NOT NULL,
                "ProviderName" character varying({FeatureValueRecordConstants.ProviderNameMaxLength}) NOT NULL,
                "ProviderKey" character varying({FeatureValueRecordConstants.ProviderKeyMaxLength}),
                "DateCreated" timestamp with time zone NOT NULL,
                "DateUpdated" timestamp with time zone,
                CONSTRAINT "PK_{options.FeatureValuesTableName}" PRIMARY KEY ("Id")
            );
            """;
    }

    private static string _CreateIndexesScript(FeaturesStorageOptions options)
    {
        var valuesTable = _Qualified(options.Schema, options.FeatureValuesTableName);
        var definitionsTable = _Qualified(options.Schema, options.FeatureDefinitionsTableName);
        var groupsTable = _Qualified(options.Schema, options.FeatureGroupDefinitionsTableName);

        var lockResource = $"headless_features_init:{options.Schema}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.FeatureGroupDefinitionsTableName}_Name" ON {groupsTable} ("Name");
            CREATE INDEX IF NOT EXISTS "IX_{options.FeatureDefinitionsTableName}_GroupName" ON {definitionsTable} ("GroupName");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.FeatureDefinitionsTableName}_Name" ON {definitionsTable} ("Name");
            CREATE INDEX IF NOT EXISTS "IX_{options.FeatureValuesTableName}_ProviderName_ProviderKey" ON {valuesTable} ("ProviderName", "ProviderKey");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.FeatureValuesTableName}_Name_ProviderName_ProviderKey" ON {valuesTable} ("Name", "ProviderName", "ProviderKey") WHERE "ProviderKey" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.FeatureValuesTableName}_Name_ProviderName_NullProviderKey" ON {valuesTable} ("Name", "ProviderName") WHERE "ProviderKey" IS NULL;
            """;
    }

    /// <summary>Returns the fully-qualified <c>"schema"."table"</c> identifier for <paramref name="tableName"/>.</summary>
    /// <param name="options">Storage options supplying the schema name.</param>
    /// <param name="tableName">Unqualified table name.</param>
    /// <returns>A double-quoted, schema-qualified table identifier safe for PostgreSQL DDL/DML.</returns>
    internal static string Qualified(FeaturesStorageOptions options, string tableName)
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
        EventName = "PostgreSqlFeaturesSchemaRaceObserved",
        Level = LogLevel.Information,
        Message = "PostgreSql features initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Treating schema as initialized."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
