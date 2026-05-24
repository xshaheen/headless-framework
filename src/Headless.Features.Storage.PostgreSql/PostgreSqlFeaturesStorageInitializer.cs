// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
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
        _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

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
        catch (PostgresException ex) when (ex.SqlState is "42P06" or "42P07" or "42710")
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static string _CreateScript(FeaturesStorageOptions options)
    {
        var valuesTable = _Qualified(options.Schema, options.FeatureValuesTableName);
        var definitionsTable = _Qualified(options.Schema, options.FeatureDefinitionsTableName);
        var groupsTable = _Qualified(options.Schema, options.FeatureGroupDefinitionsTableName);

        return $"""
            CREATE SCHEMA IF NOT EXISTS "{options.Schema}";

            CREATE TABLE IF NOT EXISTS {groupsTable} (
                "Id" uuid NOT NULL,
                "Name" character varying(128) NOT NULL,
                "DisplayName" character varying(256) NOT NULL,
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_{options.FeatureGroupDefinitionsTableName}" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS {definitionsTable} (
                "Id" uuid NOT NULL,
                "GroupName" character varying(128) NOT NULL,
                "Name" character varying(128) NOT NULL,
                "DisplayName" character varying(256) NOT NULL,
                "ParentName" character varying(128),
                "Description" character varying(256),
                "DefaultValue" character varying(256),
                "IsVisibleToClients" boolean NOT NULL,
                "IsAvailableToHost" boolean NOT NULL,
                "Providers" character varying(256),
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_{options.FeatureDefinitionsTableName}" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS {valuesTable} (
                "Id" uuid NOT NULL,
                "Name" character varying(128) NOT NULL,
                "Value" character varying(128) NOT NULL,
                "ProviderName" character varying(64) NOT NULL,
                "ProviderKey" character varying(64),
                CONSTRAINT "PK_{options.FeatureValuesTableName}" PRIMARY KEY ("Id")
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.FeatureGroupDefinitionsTableName}_Name" ON {groupsTable} ("Name");
            CREATE INDEX IF NOT EXISTS "IX_{options.FeatureDefinitionsTableName}_GroupName" ON {definitionsTable} ("GroupName");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.FeatureDefinitionsTableName}_Name" ON {definitionsTable} ("Name");
            CREATE INDEX IF NOT EXISTS "IX_{options.FeatureValuesTableName}_ProviderName_ProviderKey" ON {valuesTable} ("ProviderName", "ProviderKey");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.FeatureValuesTableName}_Name_ProviderName_ProviderKey" ON {valuesTable} ("Name", "ProviderName", "ProviderKey");
            """;
    }

    internal static string Qualified(FeaturesStorageOptions options, string tableName) => _Qualified(options.Schema, tableName);

    private static string _Qualified(string schema, string tableName) => $@"""{schema}"".""{tableName}""";
}
