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
        var valuesName = _ValuesName(options);
        var definitionsName = _DefinitionsName(options);
        var groupsName = _GroupsName(options);

        // Serialize concurrent-startup DDL across replicas with a transaction-scoped advisory
        // lock keyed on the schema (multiple tables share the schema). Auto-released on COMMIT/ROLLBACK.
        var lockResource = $"headless_features_init:{options.Schema}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            {PostgreSqlSchemaInitLock.AcquireStatement(options.Schema)}
            CREATE SCHEMA IF NOT EXISTS "{options.Schema}";

            CREATE TABLE IF NOT EXISTS {_Qualified(options.Schema, groupsName)} (
                "id" uuid NOT NULL,
                "name" character varying({FeatureGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "display_name" character varying({FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "extra_properties" text NOT NULL,
                CONSTRAINT "pk_{groupsName}" PRIMARY KEY ("id")
            );

            CREATE TABLE IF NOT EXISTS {_Qualified(options.Schema, definitionsName)} (
                "id" uuid NOT NULL,
                "group_name" character varying({FeatureGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "name" character varying({FeatureDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "display_name" character varying({FeatureDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "parent_name" character varying({FeatureDefinitionRecordConstants.NameMaxLength}),
                "description" character varying({FeatureDefinitionRecordConstants.DescriptionMaxLength}),
                "default_value" character varying({FeatureDefinitionRecordConstants.DefaultValueMaxLength}),
                "is_visible_to_clients" boolean NOT NULL,
                "is_available_to_host" boolean NOT NULL,
                "providers" character varying({FeatureDefinitionRecordConstants.ProvidersMaxLength}),
                "extra_properties" text NOT NULL,
                CONSTRAINT "pk_{definitionsName}" PRIMARY KEY ("id")
            );

            CREATE TABLE IF NOT EXISTS {_Qualified(options.Schema, valuesName)} (
                "id" uuid NOT NULL,
                "name" character varying({FeatureValueRecordConstants.NameMaxLength}) NOT NULL,
                "value" character varying({FeatureValueRecordConstants.ValueMaxLength}) NOT NULL,
                "provider_name" character varying({FeatureValueRecordConstants.ProviderNameMaxLength}) NOT NULL,
                "provider_key" character varying({FeatureValueRecordConstants.ProviderKeyMaxLength}),
                "created_at" timestamp with time zone NOT NULL,
                "updated_at" timestamp with time zone,
                CONSTRAINT "pk_{valuesName}" PRIMARY KEY ("id")
            );
            """;
    }

    private static string _CreateIndexesScript(FeaturesStorageOptions options)
    {
        var valuesName = _ValuesName(options);
        var definitionsName = _DefinitionsName(options);
        var groupsName = _GroupsName(options);
        var valuesTable = _Qualified(options.Schema, valuesName);
        var definitionsTable = _Qualified(options.Schema, definitionsName);
        var groupsTable = _Qualified(options.Schema, groupsName);

        var lockResource = $"headless_features_init:{options.Schema}";
        var acquireLock = $"SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));";

        return $"""
            {acquireLock}

            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{groupsName}_name" ON {groupsTable} ("name");
            CREATE INDEX IF NOT EXISTS "ix_{definitionsName}_group_name" ON {definitionsTable} ("group_name");
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{definitionsName}_name" ON {definitionsTable} ("name");
            CREATE INDEX IF NOT EXISTS "ix_{valuesName}_provider_name_provider_key" ON {valuesTable} ("provider_name", "provider_key");
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{valuesName}_name_provider_name_provider_key" ON {valuesTable} ("name", "provider_name", "provider_key") WHERE "provider_key" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{valuesName}_name_provider_name_null_provider_key" ON {valuesTable} ("name", "provider_name") WHERE "provider_key" IS NULL;
            """;
    }

    /// <summary>Returns the qualified feature values table.</summary>
    internal static string ValuesTable(FeaturesStorageOptions options)
    {
        return _Qualified(options.Schema, _ValuesName(options));
    }

    /// <summary>Returns the qualified feature definitions table.</summary>
    internal static string DefinitionsTable(FeaturesStorageOptions options)
    {
        return _Qualified(options.Schema, _DefinitionsName(options));
    }

    /// <summary>Returns the qualified feature group definitions table.</summary>
    internal static string GroupsTable(FeaturesStorageOptions options)
    {
        return _Qualified(options.Schema, _GroupsName(options));
    }

    private static string _ValuesName(FeaturesStorageOptions options)
    {
        return options.ResolveFeatureValuesTableName(StorageNamingStyle.SnakeCase);
    }

    private static string _DefinitionsName(FeaturesStorageOptions options)
    {
        return options.ResolveFeatureDefinitionsTableName(StorageNamingStyle.SnakeCase);
    }

    private static string _GroupsName(FeaturesStorageOptions options)
    {
        return options.ResolveFeatureGroupDefinitionsTableName(StorageNamingStyle.SnakeCase);
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
        EventName = "PostgreSqlFeaturesSchemaRaceObserved",
        Level = LogLevel.Information,
        Message = "PostgreSql features initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Retrying the DDL once in a fresh transaction."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
