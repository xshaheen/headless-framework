// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Settings.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Settings.SqlServer;

/// <summary>
/// Hosted initializer that creates or migrates the SQL Server schema, tables, indexes, and TVP types
/// required by the settings storage provider. Runs on startup when
/// <see cref="SettingsStorageOptions.InitializeOnStartup"/> is <see langword="true"/>. Concurrent startup
/// races are serialized via <c>sp_getapplock</c> and guarded by <c>IF NOT EXISTS</c> / <c>OBJECT_ID</c> checks.
/// </summary>
internal sealed class SqlServerSettingsStorageInitializer(
    IOptions<SqlServerSettingsOptions> providerOptions,
    IOptions<SettingsStorageOptions> storageOptions
) : HostedInitializer
{
    /// <summary>Gets a value indicating whether this initializer should run when the host starts.</summary>
    protected override bool RunOnStartup => storageOptions.Value.InitializeOnStartup;

    /// <summary>Creates the settings schema, tables, indexes, and TVP types if they do not already exist.</summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_CreateScript(storageOptions.Value), connection);
        command.CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the bracket-quoted <c>[schema].[table]</c> identifier for <paramref name="tableName"/>.</summary>
    /// <param name="options">Storage options that supply the schema name.</param>
    /// <param name="tableName">Unqualified table name.</param>
    /// <returns>A bracket-quoted, schema-qualified table reference safe for interpolation into SQL.</returns>
    internal static string Qualified(SettingsStorageOptions options, string tableName) =>
        $"[{options.Schema}].[{tableName}]";

    /// <summary>
    /// Builds the idempotent SQL Server DDL script that creates the schema, tables, indexes, and TVP types.
    /// The script acquires an exclusive <c>sp_getapplock</c> session lock, wraps all DDL in a single
    /// transaction, and releases the lock after commit.
    /// </summary>
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

        // Table-valued parameter types for batched id/name queries (single cached plan, no 2100-parameter
        // ceiling, portable to older engines). The name type is a heap (no PK) so trailing-space / collation
        // duplicate names cannot raise a PK violation the dynamic IN-list never had.
        var createTvpTypes = $"""
            BEGIN TRY
                IF TYPE_ID(N'{options.Schema}.HeadlessSettingsIdList') IS NULL
                    CREATE TYPE [{options.Schema}].[HeadlessSettingsIdList] AS TABLE ([Id] uniqueidentifier NOT NULL PRIMARY KEY);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            BEGIN TRY
                IF TYPE_ID(N'{options.Schema}.HeadlessSettingsNameList') IS NULL
                    CREATE TYPE [{options.Schema}].[HeadlessSettingsNameList] AS TABLE ([Name] nvarchar({SettingValueRecordConstants.NameMaxLength}) NOT NULL);
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

                {createTvpTypes}

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
