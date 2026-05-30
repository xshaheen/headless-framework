// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Features.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Features.SqlServer;

internal sealed class SqlServerFeaturesStorageInitializer(
    IOptions<SqlServerFeaturesOptions> providerOptions,
    IOptions<FeaturesStorageOptions> storageOptions
) : IHostedLifecycleService, IInitializer
{
    private volatile TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            // Pass CancellationToken.None so the prior promise's OperationCanceledException is not
            // misleadingly attributed to the current run's startup token.
            previous.TrySetCanceled(CancellationToken.None);
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

    internal static string Qualified(FeaturesStorageOptions options, string tableName) =>
        $"[{options.Schema}].[{tableName}]";

    private static string _CreateScript(FeaturesStorageOptions options)
    {
        var valuesTable = Qualified(options, options.FeatureValuesTableName);
        var definitionsTable = Qualified(options, options.FeatureDefinitionsTableName);
        var groupsTable = Qualified(options, options.FeatureGroupDefinitionsTableName);
        var valuesObject = $"{options.Schema}.{options.FeatureValuesTableName}";
        var definitionsObject = $"{options.Schema}.{options.FeatureDefinitionsTableName}";
        var groupsObject = $"{options.Schema}.{options.FeatureGroupDefinitionsTableName}";

        // Serialize concurrent-startup DDL across replicas with a session-scoped advisory lock.
        // Without this, multiple hosts racing CREATE INDEX on the same table deadlock on schema-mod
        // locks (error 1205). The outer TRY/CATCH below guarantees the lock is released on the
        // failure path; connection-close auto-release is a backstop, not the primary mechanism.
        // Lock keyed on the schema (multiple tables share it).
        var lockResource = $"headless_features_init:{options.Schema}";
        var acquireLock = $"""
            DECLARE @lockResult int;
            EXEC @lockResult = sp_getapplock @Resource = N'{lockResource}', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 30000;
            IF @lockResult < 0 THROW 50000, N'Headless.Features: failed to acquire init lock on the features schema. Another initializer may be holding it.', 1;
            """;
        var releaseLock = $"EXEC sp_releaseapplock @Resource = N'{lockResource}', @LockOwner = N'Session';";

        var ddlBody = $"""
            BEGIN TRY
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'{options.Schema}')
                    EXEC(N'CREATE SCHEMA [{options.Schema}]');
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{groupsObject}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {groupsTable} (
                        [Id] uniqueidentifier NOT NULL,
                        [Name] nvarchar({FeatureGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                        [DisplayName] nvarchar({FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                        [ExtraProperties] nvarchar(max) NOT NULL,
                        CONSTRAINT [PK_{options.FeatureGroupDefinitionsTableName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{definitionsObject}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {definitionsTable} (
                        [Id] uniqueidentifier NOT NULL,
                        [GroupName] nvarchar({FeatureGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                        [Name] nvarchar({FeatureDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                        [DisplayName] nvarchar({FeatureDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                        [ParentName] nvarchar({FeatureDefinitionRecordConstants.NameMaxLength}) NULL,
                        [Description] nvarchar({FeatureDefinitionRecordConstants.DescriptionMaxLength}) NULL,
                        [DefaultValue] nvarchar({FeatureDefinitionRecordConstants.DefaultValueMaxLength}) NULL,
                        [IsVisibleToClients] bit NOT NULL,
                        [IsAvailableToHost] bit NOT NULL,
                        [Providers] nvarchar({FeatureDefinitionRecordConstants.ProvidersMaxLength}) NULL,
                        [ExtraProperties] nvarchar(max) NOT NULL,
                        CONSTRAINT [PK_{options.FeatureDefinitionsTableName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{valuesObject}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {valuesTable} (
                        [Id] uniqueidentifier NOT NULL,
                        [Name] nvarchar({FeatureValueRecordConstants.NameMaxLength}) NOT NULL,
                        [Value] nvarchar({FeatureValueRecordConstants.ValueMaxLength}) NOT NULL,
                        [ProviderName] nvarchar({FeatureValueRecordConstants.ProviderNameMaxLength}) NOT NULL,
                        [ProviderKey] nvarchar({FeatureValueRecordConstants.ProviderKeyMaxLength}) NULL,
                        CONSTRAINT [PK_{options.FeatureValuesTableName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{options.FeatureGroupDefinitionsTableName}_Name' AND object_id = OBJECT_ID(N'{groupsObject}'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.FeatureGroupDefinitionsTableName}_Name] ON {groupsTable} ([Name] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{options.FeatureDefinitionsTableName}_GroupName' AND object_id = OBJECT_ID(N'{definitionsObject}'))
                    CREATE NONCLUSTERED INDEX [IX_{options.FeatureDefinitionsTableName}_GroupName] ON {definitionsTable} ([GroupName] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{options.FeatureDefinitionsTableName}_Name' AND object_id = OBJECT_ID(N'{definitionsObject}'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.FeatureDefinitionsTableName}_Name] ON {definitionsTable} ([Name] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{options.FeatureValuesTableName}_ProviderName_ProviderKey' AND object_id = OBJECT_ID(N'{valuesObject}'))
                    CREATE NONCLUSTERED INDEX [IX_{options.FeatureValuesTableName}_ProviderName_ProviderKey] ON {valuesTable} ([ProviderName] ASC, [ProviderKey] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{options.FeatureValuesTableName}_Name_ProviderName_ProviderKey' AND object_id = OBJECT_ID(N'{valuesObject}'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.FeatureValuesTableName}_Name_ProviderName_ProviderKey] ON {valuesTable} ([Name] ASC, [ProviderName] ASC, [ProviderKey] ASC);
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
                {ddlBody}

                {releaseLock}
            END TRY
            BEGIN CATCH
                -- Wrap the conditional release in its own TRY/CATCH so a release-side error
                -- (e.g., transient lock-state inconsistency) does NOT terminate the outer CATCH
                -- before THROW runs. Without this guard, sp_releaseapplock raising would replace
                -- the original DDL exception with the release error and hide root cause. The
                -- session-scoped applock is auto-released on connection-pool reset as backstop.
                BEGIN TRY
                    IF APPLOCK_MODE('public', N'{lockResource}', 'Session') <> 'NoLock'
                        {releaseLock}
                END TRY
                BEGIN CATCH
                    -- intentional: swallow release-side error so original DDL exception survives
                END CATCH;
                THROW;
            END CATCH;
            """;
    }
}
