// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Settings.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Settings.SqlServer;

internal sealed class SqlServerSettingsStorageInitializer(
    IOptions<SqlServerSettingsOptions> providerOptions,
    IOptions<SettingsStorageOptions> storageOptions
) : HostedInitializer
{
    protected override bool RunOnStartup => storageOptions.Value.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
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

        // Mirror the PG sibling: two filtered unique indexes split on (ProviderKey IS NOT NULL)
        // and (ProviderKey IS NULL). SqlServer's standard NULL-distinct semantics let duplicate
        // (Name, ProviderName) host-scope rows slip past a plain unique index without the filter.
        var createValuesIndexes = $"""
            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{options.SettingValuesTableName}_Name_ProviderName_ProviderKey' AND object_id = OBJECT_ID(N'{valuesObject}'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.SettingValuesTableName}_Name_ProviderName_ProviderKey] ON {valuesTable} ([Name] ASC, [ProviderName] ASC, [ProviderKey] ASC) WHERE [ProviderKey] IS NOT NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{options.SettingValuesTableName}_Name_ProviderName_NullProviderKey' AND object_id = OBJECT_ID(N'{valuesObject}'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.SettingValuesTableName}_Name_ProviderName_NullProviderKey] ON {valuesTable} ([Name] ASC, [ProviderName] ASC) WHERE [ProviderKey] IS NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            """;

        // Wrap the DDL body in BEGIN TRAN / COMMIT TRAN so a mid-script failure cannot leave the
        // schema half-initialized. Inner BEGIN TRY swallow-lists keep soft errors (2714, 1913,
        // 2759) from dooming the outer transaction.
        return $"""
            {acquireLock}

            BEGIN TRY
                BEGIN TRAN;

                {createSchema}

                {createDefinitionsTable}

                {createValuesTable}

                {createDefinitionsIndex}

                {createValuesIndexes}

                COMMIT TRAN;

                {releaseLock}
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0 ROLLBACK TRAN;

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
