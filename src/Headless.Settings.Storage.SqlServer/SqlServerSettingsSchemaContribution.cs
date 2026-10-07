// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.SqlServer;

namespace Headless.Settings.SqlServer;

/// <summary>
/// The Settings feature's schema contribution for SQL Server: the definition and value tables and their unique indexes,
/// as two idempotent steps the Headless schema runner applies.
/// </summary>
internal static class SqlServerSettingsSchemaContribution
{
    public const string TablesStepVersion = "1";
    public const string IndexesStepVersion = "2";

    private static readonly RelationalSettingsTables _Defaults = new(
        SqlServerDialect.Instance,
        new SettingsStorageOptions()
    );

    public static SchemaContribution Create(
        SqlServerSettingsOptions providerOptions,
        RelationalSettingsTables tables,
        bool applyOnStartup
    )
    {
        var schema = tables.Schema;
        var valuesName = tables.ValuesName;
        var definitionsName = tables.DefinitionsName;
        var valuesTable = tables.Values;
        var definitionsTable = tables.Definitions;
        var valuesObject = $"{schema}.{valuesName}";
        var definitionsObject = $"{schema}.{definitionsName}";

        var tablesSql = $"""
            IF OBJECT_ID(N'{definitionsObject}', N'U') IS NULL
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
                    CONSTRAINT [PK_{definitionsName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                );

            IF OBJECT_ID(N'{valuesObject}', N'U') IS NULL
                CREATE TABLE {valuesTable} (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar({SettingValueRecordConstants.NameMaxLength}) NOT NULL,
                    [Value] nvarchar({SettingValueRecordConstants.ValueMaxLength}) NOT NULL,
                    [ProviderName] nvarchar({SettingValueRecordConstants.ProviderNameMaxLength}) NOT NULL,
                    [ProviderKey] nvarchar({SettingValueRecordConstants.ProviderKeyMaxLength}) NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [UpdatedAt] datetimeoffset NULL,
                    CONSTRAINT [PK_{valuesName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                );
            """;

        // Mirror the PG sibling: two filtered unique indexes split on (ProviderKey IS NOT NULL) and
        // (ProviderKey IS NULL). SqlServer's standard NULL-distinct semantics let duplicate (Name, ProviderName)
        // host-scope rows slip past a plain unique index without the filter.
        var indexesSql = $"""
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
                "Settings",
                (valuesName, _Defaults.ValuesName),
                (definitionsName, _Defaults.DefinitionsName)
            ),
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: () => SqlServerDialect.Instance.CreateConnection(providerOptions.ConnectionString),
            schema: schema,
            steps:
            [
                new SchemaStep(TablesStepVersion, "Create the setting definition and value tables.", tablesSql),
                new SchemaStep(
                    IndexesStepVersion,
                    "Create the setting definition and value unique indexes.",
                    indexesSql
                ),
            ],
            applyOnStartup: applyOnStartup,
            hostStateTables: [definitionsName]
        );
    }
}
