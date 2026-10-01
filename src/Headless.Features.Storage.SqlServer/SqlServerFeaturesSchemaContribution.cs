// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Headless.Features.Repositories;
using Headless.Hosting.Initialization.Schema;
using Headless.Sql.SqlServer;

namespace Headless.Features.SqlServer;

/// <summary>
/// The Features feature's schema contribution for SQL Server: the value, definition, and group tables and their
/// indexes, as one idempotent step the Headless schema runner applies.
/// </summary>
internal static class SqlServerFeaturesSchemaContribution
{
    public const string StepVersion = "1";

    private static readonly RelationalFeaturesTables _Defaults = new(
        SqlServerDialect.Instance,
        new FeaturesStorageOptions()
    );

    public static SchemaContribution Create(
        SqlServerFeaturesOptions options,
        RelationalFeaturesTables tables,
        bool applyOnStartup
    )
    {
        var valuesName = tables.ValuesName;
        var definitionsName = tables.DefinitionsName;
        var groupsName = tables.GroupsName;
        var valuesTable = tables.Values;
        var definitionsTable = tables.Definitions;
        var groupsTable = tables.Groups;
        var schema = tables.Schema;
        var valuesObject = $"{schema}.{valuesName}";
        var definitionsObject = $"{schema}.{definitionsName}";
        var groupsObject = $"{schema}.{groupsName}";

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


            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Features",
                (valuesName, _Defaults.ValuesName),
                (definitionsName, _Defaults.DefinitionsName),
                (groupsName, _Defaults.GroupsName)
            ),
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: () => SqlServerDialect.Instance.CreateConnection(options.ConnectionString),
            schema: schema,
            steps:
            [
                new SchemaStep(StepVersion, "Create the value, definition, and group tables and their indexes.", sql),
            ],
            applyOnStartup: applyOnStartup
        );
    }
}
