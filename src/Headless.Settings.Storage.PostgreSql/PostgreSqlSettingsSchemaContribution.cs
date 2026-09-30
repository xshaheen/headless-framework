// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Settings.Entities;
using Headless.Sql.PostgreSql;

namespace Headless.Settings.PostgreSql;

/// <summary>
/// The Settings feature's schema contribution for PostgreSQL: the definition and value tables and their unique
/// indexes, as two idempotent steps the Headless schema runner applies.
/// </summary>
internal static class PostgreSqlSettingsSchemaContribution
{
    public const string TablesStepVersion = "1";
    public const string IndexesStepVersion = "2";

    public static SchemaContribution Create(
        PostgreSqlSettingsOptions providerOptions,
        SettingsStorageOptions storageOptions
    )
    {
        var valuesName = PostgreSqlSettingsSchema.ValuesName(storageOptions);
        var definitionsName = PostgreSqlSettingsSchema.DefinitionsName(storageOptions);
        var valuesTable = PostgreSqlSettingsSchema.ValuesTable(storageOptions);
        var definitionsTable = PostgreSqlSettingsSchema.DefinitionsTable(storageOptions);

        var tablesSql = $"""
            CREATE TABLE IF NOT EXISTS {definitionsTable} (
                "id" uuid NOT NULL,
                "name" character varying({SettingDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "display_name" character varying({SettingDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "description" character varying({SettingDefinitionRecordConstants.DescriptionMaxLength}),
                "default_value" character varying({SettingDefinitionRecordConstants.DefaultValueMaxLength}),
                "is_visible_to_clients" boolean NOT NULL,
                "is_inherited" boolean NOT NULL,
                "is_encrypted" boolean NOT NULL,
                "providers" character varying({SettingDefinitionRecordConstants.ProvidersMaxLength}),
                "extra_properties" text NOT NULL,
                CONSTRAINT "pk_{definitionsName}" PRIMARY KEY ("id")
            );

            CREATE TABLE IF NOT EXISTS {valuesTable} (
                "id" uuid NOT NULL,
                "name" character varying({SettingValueRecordConstants.NameMaxLength}) NOT NULL,
                "value" character varying({SettingValueRecordConstants.ValueMaxLength}) NOT NULL,
                "provider_name" character varying({SettingValueRecordConstants.ProviderNameMaxLength}) NOT NULL,
                "provider_key" character varying({SettingValueRecordConstants.ProviderKeyMaxLength}),
                "created_at" timestamp with time zone NOT NULL,
                "updated_at" timestamp with time zone,
                CONSTRAINT "pk_{valuesName}" PRIMARY KEY ("id")
            );
            """;

        // Two partial unique indexes split on provider_key nullability: a plain unique index treats NULLs as
        // distinct, so duplicate (name, provider_name) host-scope rows would slip past it.
        var indexesSql = $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{definitionsName}_name" ON {definitionsTable} ("name");
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{valuesName}_name_provider_name_provider_key" ON {valuesTable} ("name", "provider_name", "provider_key") WHERE "provider_key" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{valuesName}_name_provider_name_null_provider_key" ON {valuesTable} ("name", "provider_name") WHERE "provider_key" IS NULL;
            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Settings",
                (valuesName, PostgreSqlSettingsSchema.DefaultValuesName),
                (definitionsName, PostgreSqlSettingsSchema.DefaultDefinitionsName)
            ),
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: providerOptions.CreateConnection,
            schema: storageOptions.Schema,
            steps:
            [
                new SchemaStep(TablesStepVersion, "Create the setting definition and value tables.", tablesSql),
                new SchemaStep(
                    IndexesStepVersion,
                    "Create the setting definition and value unique indexes.",
                    indexesSql
                ),
            ],
            applyOnStartup: storageOptions.InitializeOnStartup
        );
    }
}
