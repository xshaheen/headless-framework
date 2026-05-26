// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Settings.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Settings.SqlServer;

internal sealed class SqlServerSettingsStorageInitializer(
    IOptions<SqlServerSettingsOptions> providerOptions,
    IOptions<SettingsStorageOptions> storageOptions
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
        await using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_CreateScript(storageOptions.Value), connection)
        {
            CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds,
        };
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string Qualified(SettingsStorageOptions options, string tableName) =>
        $"[{options.Schema}].[{tableName}]";

    private static string _CreateScript(SettingsStorageOptions options)
    {
        var valuesTable = Qualified(options, options.SettingValuesTableName);
        var definitionsTable = Qualified(options, options.SettingDefinitionsTableName);
        var valuesObject = $"{options.Schema}.{options.SettingValuesTableName}";
        var definitionsObject = $"{options.Schema}.{options.SettingDefinitionsTableName}";

        // Serialize concurrent-startup DDL across replicas with a session-scoped advisory lock.
        // Without this, multiple hosts racing CREATE INDEX on the same table deadlock on schema-mod
        // locks (error 1205). The outer TRY/CATCH below guarantees the lock is released on the
        // failure path; connection-close auto-release is a backstop, not the primary mechanism.
        // Lock keyed on the schema (two tables share the schema, so per-table keys would still
        // race on CREATE SCHEMA).
        var lockResource = $"headless_settings_init:{options.Schema}";
        var acquireLock = $"""
            DECLARE @lockResult int;
            EXEC @lockResult = sp_getapplock @Resource = N'{lockResource}', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 30000;
            IF @lockResult < 0 THROW 50000, N'Headless.Settings: failed to acquire init lock on the settings schema. Another initializer may be holding it.', 1;
            """;
        var releaseLock = $"EXEC sp_releaseapplock @Resource = N'{lockResource}', @LockOwner = N'Session';";

        var createSchema = $"""
            BEGIN TRY
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'{options.Schema}')
                    EXEC(N'CREATE SCHEMA [{options.Schema}]');
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            """;

        var createDefinitionsTable = $"""
            BEGIN TRY
                IF OBJECT_ID(N'{definitionsObject}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {definitionsTable} (
                        [Id] uniqueidentifier NOT NULL,
                        [Name] nvarchar({SettingDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                        [DisplayName] nvarchar({SettingDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                        [Description] nvarchar({SettingDefinitionRecordConstants.DescriptionMaxLength}) NULL,
                        [DefaultValue] nvarchar({SettingDefinitionRecordConstants.DefaultValueMaxLength}) NULL,
                        [IsVisibleToClients] bit NOT NULL,
                        [IsInherited] bit NOT NULL,
                        [IsEncrypted] bit NOT NULL,
                        [Providers] nvarchar({SettingDefinitionRecordConstants.ProvidersMaxLength}) NULL,
                        [ExtraProperties] nvarchar(max) NOT NULL,
                        CONSTRAINT [PK_{options.SettingDefinitionsTableName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.SettingDefinitionsTableName}_Name] ON {definitionsTable} ([Name] ASC);
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            """;

        var createValuesTable = $"""
            BEGIN TRY
                IF OBJECT_ID(N'{valuesObject}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {valuesTable} (
                        [Id] uniqueidentifier NOT NULL,
                        [Name] nvarchar({SettingValueRecordConstants.NameMaxLength}) NOT NULL,
                        [Value] nvarchar({SettingValueRecordConstants.ValueMaxLength}) NOT NULL,
                        [ProviderName] nvarchar({SettingValueRecordConstants.ProviderNameMaxLength}) NOT NULL,
                        [ProviderKey] nvarchar({SettingValueRecordConstants.ProviderKeyMaxLength}) NULL,
                        [DateCreated] datetimeoffset NOT NULL,
                        [DateUpdated] datetimeoffset NULL,
                        CONSTRAINT [PK_{options.SettingValuesTableName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.SettingValuesTableName}_Name_ProviderName_ProviderKey] ON {valuesTable} ([Name] ASC, [ProviderName] ASC, [ProviderKey] ASC);
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            """;

        var createDefinitionsIndex = $"""
            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{options.SettingDefinitionsTableName}_Name' AND object_id = OBJECT_ID(N'{definitionsObject}'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.SettingDefinitionsTableName}_Name] ON {definitionsTable} ([Name] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            """;

        var createValuesIndex = $"""
            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{options.SettingValuesTableName}_Name_ProviderName_ProviderKey' AND object_id = OBJECT_ID(N'{valuesObject}'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.SettingValuesTableName}_Name_ProviderName_ProviderKey] ON {valuesTable} ([Name] ASC, [ProviderName] ASC, [ProviderKey] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            """;

        // Release the advisory lock on every path — success AND failure. Wrapping the DDL body in
        // an outer TRY/CATCH guarantees the release runs before the connection returns to the pool;
        // a Session-scoped applock that leaks past the throw would otherwise persist and starve the
        // next replica's sp_getapplock until the connection is physically reset.
        return $"""
            {acquireLock}

            BEGIN TRY
                {createSchema}

                {createDefinitionsTable}

                {createValuesTable}

                {createDefinitionsIndex}

                {createValuesIndex}

                {releaseLock}
            END TRY
            BEGIN CATCH
                IF APPLOCK_MODE('public', N'{lockResource}', 'Session') <> 'NoLock'
                    {releaseLock}
                THROW;
            END CATCH;
            """;
    }
}
