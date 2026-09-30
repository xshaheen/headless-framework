// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Headless.Hosting.Initialization;
using Headless.Hosting.Initialization.Schema;
using Headless.Sql.SqlServer;

namespace Headless.Features.SqlServer;

/// <summary>
/// The Features feature's schema contribution for SQL Server: the value, definition, and group tables, their indexes,
/// and the table-valued parameter types, as one idempotent step the Headless schema runner applies.
/// </summary>
internal static class SqlServerFeaturesSchemaContribution
{
    public const string StepVersion = "1";

    // Only the default names are read from it, so the history identity tracks the defaults FeaturesStorageOptions owns.
    private static readonly FeaturesStorageOptions _Defaults = new();

    public static SchemaContribution Create(SqlServerFeaturesOptions options, FeaturesStorageOptions storageOptions)
    {
        var valuesName = storageOptions.ResolveFeatureValuesTableName(StorageNamingStyle.PascalCase);
        var definitionsName = storageOptions.ResolveFeatureDefinitionsTableName(StorageNamingStyle.PascalCase);
        var groupsName = storageOptions.ResolveFeatureGroupDefinitionsTableName(StorageNamingStyle.PascalCase);
        var valuesTable = SqlServerFeaturesSchema.Qualified(storageOptions, valuesName);
        var definitionsTable = SqlServerFeaturesSchema.Qualified(storageOptions, definitionsName);
        var groupsTable = SqlServerFeaturesSchema.Qualified(storageOptions, groupsName);
        var schema = storageOptions.Schema;
        var valuesObject = $"{schema}.{valuesName}";
        var definitionsObject = $"{schema}.{definitionsName}";
        var groupsObject = $"{schema}.{groupsName}";

        // HeadlessFeaturesIdList and HeadlessFeaturesNameList are the table-valued parameter types of the batched
        // id and name queries. The name type is a heap (no primary key) so names that collide under trailing-space or
        // collation rules cannot raise a key violation.
        var sql = $"""
            IF OBJECT_ID(N'{groupsObject}', N'U') IS NULL
                CREATE TABLE {groupsTable} (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar({FeatureGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                    [DisplayName] nvarchar({FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                    [ExtraProperties] nvarchar(max) NOT NULL,
                    CONSTRAINT [PK_{groupsName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                );

            IF OBJECT_ID(N'{definitionsObject}', N'U') IS NULL
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
                    CONSTRAINT [PK_{definitionsName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                );

            IF OBJECT_ID(N'{valuesObject}', N'U') IS NULL
                CREATE TABLE {valuesTable} (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar({FeatureValueRecordConstants.NameMaxLength}) NOT NULL,
                    [Value] nvarchar({FeatureValueRecordConstants.ValueMaxLength}) NOT NULL,
                    [ProviderName] nvarchar({FeatureValueRecordConstants.ProviderNameMaxLength}) NOT NULL,
                    [ProviderKey] nvarchar({FeatureValueRecordConstants.ProviderKeyMaxLength}) NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [UpdatedAt] datetimeoffset NULL,
                    CONSTRAINT [PK_{valuesName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                );

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{groupsName}_Name' AND object_id = OBJECT_ID(N'{groupsObject}'))
                CREATE UNIQUE NONCLUSTERED INDEX [IX_{groupsName}_Name] ON {groupsTable} ([Name] ASC);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{definitionsName}_GroupName' AND object_id = OBJECT_ID(N'{definitionsObject}'))
                CREATE NONCLUSTERED INDEX [IX_{definitionsName}_GroupName] ON {definitionsTable} ([GroupName] ASC);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{definitionsName}_Name' AND object_id = OBJECT_ID(N'{definitionsObject}'))
                CREATE UNIQUE NONCLUSTERED INDEX [IX_{definitionsName}_Name] ON {definitionsTable} ([Name] ASC);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{valuesName}_ProviderName_ProviderKey' AND object_id = OBJECT_ID(N'{valuesObject}'))
                CREATE NONCLUSTERED INDEX [IX_{valuesName}_ProviderName_ProviderKey] ON {valuesTable} ([ProviderName] ASC, [ProviderKey] ASC);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{valuesName}_Name_ProviderName_ProviderKey' AND object_id = OBJECT_ID(N'{valuesObject}'))
                CREATE UNIQUE NONCLUSTERED INDEX [IX_{valuesName}_Name_ProviderName_ProviderKey] ON {valuesTable} ([Name] ASC, [ProviderName] ASC, [ProviderKey] ASC) WHERE [ProviderKey] IS NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{valuesName}_Name_ProviderName_NullProviderKey' AND object_id = OBJECT_ID(N'{valuesObject}'))
                CREATE UNIQUE NONCLUSTERED INDEX [IX_{valuesName}_Name_ProviderName_NullProviderKey] ON {valuesTable} ([Name] ASC, [ProviderName] ASC) WHERE [ProviderKey] IS NULL;

            IF TYPE_ID(N'{schema}.HeadlessFeaturesIdList') IS NULL
                CREATE TYPE [{schema}].[HeadlessFeaturesIdList] AS TABLE ([Id] uniqueidentifier NOT NULL PRIMARY KEY);

            IF TYPE_ID(N'{schema}.HeadlessFeaturesNameList') IS NULL
                CREATE TYPE [{schema}].[HeadlessFeaturesNameList] AS TABLE ([Name] nvarchar({FeatureValueRecordConstants.NameMaxLength}) NOT NULL);
            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Features",
                (
                    storageOptions.FeatureValuesTableName,
                    _Defaults.ResolveFeatureValuesTableName(StorageNamingStyle.PascalCase)
                ),
                (
                    storageOptions.FeatureDefinitionsTableName,
                    _Defaults.ResolveFeatureDefinitionsTableName(StorageNamingStyle.PascalCase)
                ),
                (
                    storageOptions.FeatureGroupDefinitionsTableName,
                    _Defaults.ResolveFeatureGroupDefinitionsTableName(StorageNamingStyle.PascalCase)
                )
            ),
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: options.CreateConnection,
            schema: schema,
            steps:
            [
                new SchemaStep(
                    StepVersion,
                    "Create the value, definition, and group tables, their indexes, and the id and name list types.",
                    sql
                ),
            ],
            applyOnStartup: storageOptions.InitializeOnStartup
        );
    }
}
