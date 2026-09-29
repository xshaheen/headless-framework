// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Permissions.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.SqlServer;

internal sealed class SqlServerPermissionsStorageInitializer(
    IOptions<SqlServerPermissionsOptions> providerOptions,
    IOptions<PermissionsStorageOptions> storageOptions
) : HostedInitializer
{
    protected override bool RunOnStartup => storageOptions.Value.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_CreateScript(storageOptions.Value), connection);
        command.CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string Qualified(PermissionsStorageOptions options, string tableName)
    {
        return $"[{options.Schema}].[{tableName}]";
    }

    /// <summary>Returns the qualified permission grants table.</summary>
    internal static string GrantsTable(PermissionsStorageOptions options)
    {
        return Qualified(options, options.ResolvePermissionGrantsTableName(StorageNamingStyle.PascalCase));
    }

    /// <summary>Returns the qualified permission definitions table.</summary>
    internal static string DefinitionsTable(PermissionsStorageOptions options)
    {
        return Qualified(options, options.ResolvePermissionDefinitionsTableName(StorageNamingStyle.PascalCase));
    }

    /// <summary>Returns the qualified permission group definitions table.</summary>
    internal static string GroupsTable(PermissionsStorageOptions options)
    {
        return Qualified(options, options.ResolvePermissionGroupDefinitionsTableName(StorageNamingStyle.PascalCase));
    }

    private static string _CreateScript(PermissionsStorageOptions options)
    {
        var grantsName = options.ResolvePermissionGrantsTableName(StorageNamingStyle.PascalCase);
        var grantsTable = Qualified(options, grantsName);
        var definitionsName = options.ResolvePermissionDefinitionsTableName(StorageNamingStyle.PascalCase);
        var definitionsTable = Qualified(options, definitionsName);
        var groupsName = options.ResolvePermissionGroupDefinitionsTableName(StorageNamingStyle.PascalCase);
        var groupsTable = Qualified(options, groupsName);
        var grantsObject = $"{options.Schema}.{grantsName}";
        var definitionsObject = $"{options.Schema}.{definitionsName}";
        var groupsObject = $"{options.Schema}.{groupsName}";

        // Serialize concurrent-startup DDL across replicas with a session-scoped advisory lock.
        var lockResource = $"headless_permissions_init:{options.Schema}";
        var acquireLock = $"""
            DECLARE @lockResult int;
            EXEC @lockResult = sp_getapplock @Resource = N'{lockResource}', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 30000;
            IF @lockResult < 0 THROW 50000, N'Headless.Permissions: failed to acquire init lock on the permissions schema. Another initializer may be holding it.', 1;
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
                        [Name] nvarchar({PermissionGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                        [DisplayName] nvarchar({PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                        [ExtraProperties] nvarchar(max) NOT NULL,
                        CONSTRAINT [PK_{groupsName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{groupsName}_Name' AND object_id = OBJECT_ID(N'{groupsObject}'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{groupsName}_Name] ON {groupsTable} ([Name] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{definitionsObject}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {definitionsTable} (
                        [Id] uniqueidentifier NOT NULL,
                        [GroupName] nvarchar({PermissionGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                        [Name] nvarchar({PermissionDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                        [DisplayName] nvarchar({PermissionDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                        [IsEnabled] bit NOT NULL,
                        [ParentName] nvarchar({PermissionDefinitionRecordConstants.NameMaxLength}) NULL,
                        [Providers] nvarchar({PermissionDefinitionRecordConstants.ProvidersMaxLength}) NULL,
                        [ExtraProperties] nvarchar(max) NOT NULL,
                        CONSTRAINT [PK_{definitionsName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{definitionsName}_GroupName' AND object_id = OBJECT_ID(N'{definitionsObject}'))
                    CREATE NONCLUSTERED INDEX [IX_{definitionsName}_GroupName] ON {definitionsTable} ([GroupName] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{definitionsName}_Name' AND object_id = OBJECT_ID(N'{definitionsObject}'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{definitionsName}_Name] ON {definitionsTable} ([Name] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{grantsObject}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {grantsTable} (
                        [Id] uniqueidentifier NOT NULL,
                        [Name] nvarchar({PermissionGrantRecordConstants.NameMaxLength}) NOT NULL,
                        [ProviderName] nvarchar({PermissionGrantRecordConstants.ProviderNameMaxLength}) NOT NULL,
                        [ProviderKey] nvarchar({PermissionGrantRecordConstants.ProviderKeyMaxLength}) NOT NULL,
                        [TenantId] nvarchar({PermissionGrantRecordConstants.TenantIdMaxLength}) NULL,
                        [IsGranted] bit NOT NULL DEFAULT CAST(1 AS bit),
                        [CreatedAt] datetimeoffset NOT NULL,
                        [UpdatedAt] datetimeoffset NULL,
                        CONSTRAINT [PK_{grantsName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{grantsName}_TenantId_Name_ProviderName_ProviderKey' AND object_id = OBJECT_ID(N'{grantsObject}'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{grantsName}_TenantId_Name_ProviderName_ProviderKey] ON {grantsTable} ([TenantId] ASC, [Name] ASC, [ProviderName] ASC, [ProviderKey] ASC) WHERE [TenantId] IS NOT NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            BEGIN TRY
                -- Mirror the PG sibling: a filtered unique index for host-scoped grants (TenantId
                -- IS NULL) so SqlServer's standard NULL-distinct semantics don't allow duplicate
                -- host grants for the same (Name, ProviderName, ProviderKey).
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{grantsName}_Name_ProviderName_ProviderKey_NoTenant' AND object_id = OBJECT_ID(N'{grantsObject}'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{grantsName}_Name_ProviderName_ProviderKey_NoTenant] ON {grantsTable} ([Name] ASC, [ProviderName] ASC, [ProviderKey] ASC) WHERE [TenantId] IS NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            -- Table-valued parameter types for batched id/name queries (single cached plan, no 2100-parameter
            -- ceiling, portable to older engines). The name type is a heap (no PK) so trailing-space / collation
            -- duplicate names cannot raise a PK violation the dynamic IN-list never had.
            BEGIN TRY
                IF TYPE_ID(N'{options.Schema}.HeadlessPermissionsIdList') IS NULL
                    CREATE TYPE [{options.Schema}].[HeadlessPermissionsIdList] AS TABLE ([Id] uniqueidentifier NOT NULL PRIMARY KEY);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            BEGIN TRY
                IF TYPE_ID(N'{options.Schema}.HeadlessPermissionsNameList') IS NULL
                    CREATE TYPE [{options.Schema}].[HeadlessPermissionsNameList] AS TABLE ([Name] nvarchar({PermissionGrantRecordConstants.NameMaxLength}) NOT NULL);
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
