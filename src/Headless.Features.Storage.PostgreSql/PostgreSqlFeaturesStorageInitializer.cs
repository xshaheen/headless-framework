// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.Features.Entities;
using Headless.Hosting.Initialization;
using Headless.Sql.PostgreSql;
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

        // Split table-creation DDL from index-creation DDL into separate transactions, so a race absorbed on
        // one side rolls back and reruns only that side's batch.
        await _RunSchemaAndTablesAsync(connection, options, cancellationToken).ConfigureAwait(false);
        await _RunIndexesAsync(connection, options, cancellationToken).ConfigureAwait(false);
    }

    private Task _RunSchemaAndTablesAsync(
        NpgsqlConnection connection,
        FeaturesStorageOptions options,
        CancellationToken cancellationToken
    )
    {
        return _RunDdlAsync(connection, _CreateSchemaAndTablesScript(options), cancellationToken);
    }

    private Task _RunIndexesAsync(
        NpgsqlConnection connection,
        FeaturesStorageOptions options,
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

            {PostgreSqlSchemaInitLock.AcquireStatement(options.Schema)}
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
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone,
                CONSTRAINT "PK_{options.FeatureValuesTableName}" PRIMARY KEY ("Id")
            );

            DO $migration$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = '{options.Schema}' AND table_name = '{options.FeatureValuesTableName}' AND column_name = 'DateCreated'
                ) AND NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = '{options.Schema}' AND table_name = '{options.FeatureValuesTableName}' AND column_name = 'CreatedAt'
                ) THEN
                    ALTER TABLE {valuesTable} RENAME COLUMN "DateCreated" TO "CreatedAt";
                END IF;

                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = '{options.Schema}' AND table_name = '{options.FeatureValuesTableName}' AND column_name = 'DateUpdated'
                ) AND NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = '{options.Schema}' AND table_name = '{options.FeatureValuesTableName}' AND column_name = 'UpdatedAt'
                ) THEN
                    ALTER TABLE {valuesTable} RENAME COLUMN "DateUpdated" TO "UpdatedAt";
                END IF;
            END $migration$;
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
        Message = "PostgreSql features initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Retrying the DDL once in a fresh transaction."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
