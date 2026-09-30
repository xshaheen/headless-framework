// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Headless.Hosting.Initialization;
using Headless.Hosting.Initialization.Schema;
using Headless.Sql.PostgreSql;

namespace Headless.Features.PostgreSql;

/// <summary>
/// The Features feature's schema contribution for PostgreSQL: the value, definition, and group tables, then their
/// indexes, as idempotent steps the Headless schema runner applies.
/// </summary>
internal static class PostgreSqlFeaturesSchemaContribution
{
    public const string TablesStepVersion = "1";
    public const string IndexesStepVersion = "2";

    // Only the default names are read from it, so the history identity tracks the defaults FeaturesStorageOptions owns.
    private static readonly FeaturesStorageOptions _Defaults = new();

    public static SchemaContribution Create(PostgreSqlFeaturesOptions options, FeaturesStorageOptions storageOptions)
    {
        var valuesName = PostgreSqlFeaturesSchema.ValuesName(storageOptions);
        var definitionsName = PostgreSqlFeaturesSchema.DefinitionsName(storageOptions);
        var groupsName = PostgreSqlFeaturesSchema.GroupsName(storageOptions);
        var valuesTable = PostgreSqlFeaturesSchema.ValuesTable(storageOptions);
        var definitionsTable = PostgreSqlFeaturesSchema.DefinitionsTable(storageOptions);
        var groupsTable = PostgreSqlFeaturesSchema.GroupsTable(storageOptions);

        var tablesSql = $"""
            CREATE TABLE IF NOT EXISTS {groupsTable} (
                "id" uuid NOT NULL,
                "name" character varying({FeatureGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "display_name" character varying({FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "extra_properties" text NOT NULL,
                CONSTRAINT "pk_{groupsName}" PRIMARY KEY ("id")
            );

            CREATE TABLE IF NOT EXISTS {definitionsTable} (
                "id" uuid NOT NULL,
                "group_name" character varying({FeatureGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "name" character varying({FeatureDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "display_name" character varying({FeatureDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "parent_name" character varying({FeatureDefinitionRecordConstants.NameMaxLength}),
                "description" character varying({FeatureDefinitionRecordConstants.DescriptionMaxLength}),
                "default_value" character varying({FeatureDefinitionRecordConstants.DefaultValueMaxLength}),
                "is_visible_to_clients" boolean NOT NULL,
                "is_available_to_host" boolean NOT NULL,
                "providers" character varying({FeatureDefinitionRecordConstants.ProvidersMaxLength}),
                "extra_properties" text NOT NULL,
                CONSTRAINT "pk_{definitionsName}" PRIMARY KEY ("id")
            );

            CREATE TABLE IF NOT EXISTS {valuesTable} (
                "id" uuid NOT NULL,
                "name" character varying({FeatureValueRecordConstants.NameMaxLength}) NOT NULL,
                "value" character varying({FeatureValueRecordConstants.ValueMaxLength}) NOT NULL,
                "provider_name" character varying({FeatureValueRecordConstants.ProviderNameMaxLength}) NOT NULL,
                "provider_key" character varying({FeatureValueRecordConstants.ProviderKeyMaxLength}),
                "created_at" timestamp with time zone NOT NULL,
                "updated_at" timestamp with time zone,
                CONSTRAINT "pk_{valuesName}" PRIMARY KEY ("id")
            );
            """;

        // Two partial unique indexes cover the provider key, because a plain unique index treats every NULL key as
        // distinct and would admit duplicate keyless values.
        var indexesSql = $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{groupsName}_name" ON {groupsTable} ("name");
            CREATE INDEX IF NOT EXISTS "ix_{definitionsName}_group_name" ON {definitionsTable} ("group_name");
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{definitionsName}_name" ON {definitionsTable} ("name");
            CREATE INDEX IF NOT EXISTS "ix_{valuesName}_provider_name_provider_key" ON {valuesTable} ("provider_name", "provider_key");
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{valuesName}_name_provider_name_provider_key" ON {valuesTable} ("name", "provider_name", "provider_key") WHERE "provider_key" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{valuesName}_name_provider_name_null_provider_key" ON {valuesTable} ("name", "provider_name") WHERE "provider_key" IS NULL;
            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Features",
                (
                    storageOptions.FeatureValuesTableName,
                    _Defaults.ResolveFeatureValuesTableName(StorageNamingStyle.SnakeCase)
                ),
                (
                    storageOptions.FeatureDefinitionsTableName,
                    _Defaults.ResolveFeatureDefinitionsTableName(StorageNamingStyle.SnakeCase)
                ),
                (
                    storageOptions.FeatureGroupDefinitionsTableName,
                    _Defaults.ResolveFeatureGroupDefinitionsTableName(StorageNamingStyle.SnakeCase)
                )
            ),
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: options.CreateConnection,
            schema: storageOptions.Schema,
            steps:
            [
                new SchemaStep(TablesStepVersion, "Create the value, definition, and group tables.", tablesSql),
                new SchemaStep(IndexesStepVersion, "Create the value, definition, and group indexes.", indexesSql),
            ],
            applyOnStartup: storageOptions.InitializeOnStartup
        );
    }
}
