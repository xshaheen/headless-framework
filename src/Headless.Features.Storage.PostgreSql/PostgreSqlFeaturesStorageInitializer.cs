// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Features.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Features.PostgreSql;

internal sealed class PostgreSqlFeaturesStorageInitializer(
    IOptions<PostgreSqlFeaturesOptions> providerOptions,
    IOptions<FeaturesStorageOptions> storageOptions
) : IHostedLifecycleService, IInitializer
{
    private TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsInitialized { get; private set; }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        // On a host restart, swap the completion source atomically and cancel the previous promise
        // so waiters from the prior run observe OperationCanceledException rather than hanging.
        // On first start, _completion is the field initializer (no prior waiters to rescue), so
        // skip the cancel — a fresh TCS is never IsCompleted.
        if (_completion.Task.IsCompleted)
        {
            var previous = Interlocked.Exchange(
                ref _completion,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            previous.TrySetCanceled(cancellationToken);
        }

        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            IsInitialized = true;
            _completion.TrySetResult();
        }
        catch (Exception ex)
        {
            _completion.TrySetException(ex);
            throw;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task WaitForInitializationAsync(CancellationToken cancellationToken = default)
    {
        await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var options = storageOptions.Value;
        var sql = _CreateScript(options);
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
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
        // 42P06: schema_already_exists, 42P07: relation_already_exists, 42710: duplicate_object,
        // 23505: unique_violation on pg_namespace_nspname_index when two transactions race
        // CREATE SCHEMA IF NOT EXISTS (the IF NOT EXISTS check is not transactional with the
        // catalog insert). The pg_advisory_xact_lock in _CreateScript serializes ours, but a
        // foreign initializer running concurrent DDL can still trigger this path — absorb it
        // and treat the schema as initialized.
        catch (PostgresException ex) when (ex.SqlState is "42P06" or "42P07" or "42710" or "23505")
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static string _CreateScript(FeaturesStorageOptions options)
    {
        var valuesTable = _Qualified(options.Schema, options.FeatureValuesTableName);
        var definitionsTable = _Qualified(options.Schema, options.FeatureDefinitionsTableName);
        var groupsTable = _Qualified(options.Schema, options.FeatureGroupDefinitionsTableName);

        // Serialize concurrent-startup DDL across replicas with a transaction-scoped advisory
        // lock keyed on the schema (multiple tables share the schema, so a per-table key would
        // still race on CREATE SCHEMA). Auto-released on COMMIT/ROLLBACK; no explicit release.
        var lockResource = $"headless_features_init:{options.Schema}";
        var acquireLock = $"""SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));""";

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
                CONSTRAINT "PK_{options.FeatureValuesTableName}" PRIMARY KEY ("Id")
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.FeatureGroupDefinitionsTableName}_Name" ON {groupsTable} ("Name");
            CREATE INDEX IF NOT EXISTS "IX_{options.FeatureDefinitionsTableName}_GroupName" ON {definitionsTable} ("GroupName");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.FeatureDefinitionsTableName}_Name" ON {definitionsTable} ("Name");
            CREATE INDEX IF NOT EXISTS "IX_{options.FeatureValuesTableName}_ProviderName_ProviderKey" ON {valuesTable} ("ProviderName", "ProviderKey");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.FeatureValuesTableName}_Name_ProviderName_ProviderKey" ON {valuesTable} ("Name", "ProviderName", "ProviderKey") WHERE "ProviderKey" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.FeatureValuesTableName}_Name_ProviderName_NullProviderKey" ON {valuesTable} ("Name", "ProviderName") WHERE "ProviderKey" IS NULL;
            """;
    }

    internal static string Qualified(FeaturesStorageOptions options, string tableName) => _Qualified(options.Schema, tableName);

    private static string _Qualified(string schema, string tableName) => $@"""{schema}"".""{tableName}""";
}
