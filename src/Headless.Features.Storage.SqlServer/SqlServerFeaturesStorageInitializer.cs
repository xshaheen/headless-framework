// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Features.SqlServer;

/// <summary>
/// Hosted initializer that creates or ensures the SQL Server schema, tables, indexes, and
/// table-valued type required by the features storage provider. Runs on startup when
/// <see cref="FeaturesStorageOptions.InitializeOnStartup"/> is <see langword="true"/>.
/// Uses <c>sp_getapplock</c> to serialize concurrent-startup DDL across replicas.
/// </summary>
internal sealed class SqlServerFeaturesStorageInitializer(
    IOptions<SqlServerFeaturesOptions> providerOptions,
    IOptions<FeaturesStorageOptions> storageOptions
) : HostedInitializer
{
    /// <inheritdoc/>
    protected override bool RunOnStartup => storageOptions.Value.InitializeOnStartup;

    /// <summary>
    /// Creates or ensures the SQL Server schema, tables, indexes, and
    /// <c>HeadlessFeaturesIdList</c> table-valued type. The script acquires a session-scoped
    /// application lock, executes all DDL inside a single transaction, and releases the lock on
    /// success. On failure the transaction is rolled back and the lock is released before
    /// re-throwing.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_CreateScript(storageOptions.Value), connection);
        command.CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the fully-qualified <c>[schema].[table]</c> identifier for <paramref name="tableName"/>.</summary>
    /// <param name="options">Storage options supplying the schema name.</param>
    /// <param name="tableName">Unqualified table name.</param>
    /// <returns>A bracket-quoted, schema-qualified table identifier safe for SQL Server DDL/DML.</returns>
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
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.FeatureValuesTableName}_Name_ProviderName_ProviderKey] ON {valuesTable} ([Name] ASC, [ProviderName] ASC, [ProviderKey] ASC) WHERE [ProviderKey] IS NOT NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{options.FeatureValuesTableName}_Name_ProviderName_NullProviderKey' AND object_id = OBJECT_ID(N'{valuesObject}'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.FeatureValuesTableName}_Name_ProviderName_NullProviderKey] ON {valuesTable} ([Name] ASC, [ProviderName] ASC) WHERE [ProviderKey] IS NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF TYPE_ID(N'{options.Schema}.HeadlessFeaturesIdList') IS NULL
                    CREATE TYPE [{options.Schema}].[HeadlessFeaturesIdList] AS TABLE ([Id] uniqueidentifier NOT NULL PRIMARY KEY);
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

                {ddlBody}

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
