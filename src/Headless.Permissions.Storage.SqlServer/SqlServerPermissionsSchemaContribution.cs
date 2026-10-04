// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.SqlServer;

namespace Headless.Permissions.SqlServer;

/// <summary>
/// The Permissions feature's schema contribution for SQL Server: the grant, definition, and group tables and their
/// indexes, as one idempotent step the Headless schema runner applies.
/// </summary>
internal static class SqlServerPermissionsSchemaContribution
{
    public const string StepVersion = "1";

    private static readonly RelationalPermissionsTables _Defaults = new(
        SqlServerDialect.Instance,
        new PermissionsStorageOptions()
    );

    public static SchemaContribution Create(
        SqlServerPermissionsOptions options,
        RelationalPermissionsTables tables,
        bool applyOnStartup
    )
    {
        var grantsName = tables.GrantsName;
        var definitionsName = tables.DefinitionsName;
        var groupsName = tables.GroupsName;
        var grantsTable = tables.Grants;
        var definitionsTable = tables.Definitions;
        var groupsTable = tables.Groups;
        var schema = tables.Schema;
        var grantsObject = $"{schema}.{grantsName}";
        var definitionsObject = $"{schema}.{definitionsName}";
        var groupsObject = $"{schema}.{groupsName}";

        var sql = $"""
            IF OBJECT_ID(N'{groupsObject}', N'U') IS NULL
                CREATE TABLE {groupsTable} (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar({PermissionGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                    [DisplayName] nvarchar({PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                    [ExtraProperties] nvarchar(max) NOT NULL,
                    CONSTRAINT [PK_{groupsName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                );

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{groupsName}_Name' AND object_id = OBJECT_ID(N'{groupsObject}'))
                CREATE UNIQUE NONCLUSTERED INDEX [IX_{groupsName}_Name] ON {groupsTable} ([Name] ASC);

            IF OBJECT_ID(N'{definitionsObject}', N'U') IS NULL
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

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{definitionsName}_GroupName' AND object_id = OBJECT_ID(N'{definitionsObject}'))
                CREATE NONCLUSTERED INDEX [IX_{definitionsName}_GroupName] ON {definitionsTable} ([GroupName] ASC);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{definitionsName}_Name' AND object_id = OBJECT_ID(N'{definitionsObject}'))
                CREATE UNIQUE NONCLUSTERED INDEX [IX_{definitionsName}_Name] ON {definitionsTable} ([Name] ASC);

            IF OBJECT_ID(N'{grantsObject}', N'U') IS NULL
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

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{grantsName}_TenantId_Name_ProviderName_ProviderKey' AND object_id = OBJECT_ID(N'{grantsObject}'))
                CREATE UNIQUE NONCLUSTERED INDEX [IX_{grantsName}_TenantId_Name_ProviderName_ProviderKey] ON {grantsTable} ([TenantId] ASC, [Name] ASC, [ProviderName] ASC, [ProviderKey] ASC) WHERE [TenantId] IS NOT NULL;

            -- Mirror the PG sibling: a filtered unique index for host-scoped grants (TenantId
            -- IS NULL) so SqlServer's standard NULL-distinct semantics don't allow duplicate
            -- host grants for the same (Name, ProviderName, ProviderKey).
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{grantsName}_Name_ProviderName_ProviderKey_NoTenant' AND object_id = OBJECT_ID(N'{grantsObject}'))
                CREATE UNIQUE NONCLUSTERED INDEX [IX_{grantsName}_Name_ProviderName_ProviderKey_NoTenant] ON {grantsTable} ([Name] ASC, [ProviderName] ASC, [ProviderKey] ASC) WHERE [TenantId] IS NULL;

            -- Table-valued parameter types for batched id/name queries (single cached plan, no 2100-parameter
            -- ceiling, portable to older engines). The name type is a heap (no PK) so trailing-space / collation
            -- duplicate names cannot raise a PK violation the dynamic IN-list never had.

            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Permissions",
                (grantsName, _Defaults.GrantsName),
                (definitionsName, _Defaults.DefinitionsName),
                (groupsName, _Defaults.GroupsName)
            ),
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: () => SqlServerDialect.Instance.CreateConnection(options.ConnectionString),
            schema: schema,
            steps:
            [
                new SchemaStep(StepVersion, "Create the grant, definition, and group tables and their indexes.", sql),
            ],
            applyOnStartup: applyOnStartup
        );
    }
}
