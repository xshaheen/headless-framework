// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Settings.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Settings.PostgreSql;

internal sealed class PostgreSqlSettingsStorageInitializer(
    IOptions<PostgreSqlSettingsOptions> providerOptions,
    IOptions<SettingsStorageOptions> storageOptions
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

    private static string _CreateScript(SettingsStorageOptions options)
    {
        var valuesTable = _Qualified(options.Schema, options.SettingValuesTableName);
        var definitionsTable = _Qualified(options.Schema, options.SettingDefinitionsTableName);

        return $"""
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

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.SettingDefinitionsTableName}_Name" ON {definitionsTable} ("Name");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.SettingValuesTableName}_Name_ProviderName_ProviderKey" ON {valuesTable} ("Name", "ProviderName", "ProviderKey") WHERE "ProviderKey" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.SettingValuesTableName}_Name_ProviderName_NullProviderKey" ON {valuesTable} ("Name", "ProviderName") WHERE "ProviderKey" IS NULL;
            """;
    }

    internal static string Qualified(SettingsStorageOptions options, string tableName) => _Qualified(options.Schema, tableName);

    private static string _Qualified(string schema, string tableName) => $@"""{schema}"".""{tableName}""";
}
