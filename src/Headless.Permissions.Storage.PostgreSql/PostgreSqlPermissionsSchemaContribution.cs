// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.PostgreSql;

namespace Headless.Permissions.PostgreSql;

/// <summary>
/// The Permissions feature's schema contribution for PostgreSQL: the grant, definition, and group tables, then their
/// indexes, as idempotent steps the Headless schema runner applies.
/// </summary>
internal static class PostgreSqlPermissionsSchemaContribution
{
    public const string TablesStepVersion = "1";
    public const string IndexesStepVersion = "2";

    private static readonly RelationalPermissionsTables _Defaults = new(
        PostgreSqlDialect.Instance,
        new PermissionsStorageOptions()
    );

    public static SchemaContribution Create(
        PostgreSqlPermissionsOptions options,
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

        var tablesSql = $"""
            CREATE TABLE IF NOT EXISTS {groupsTable} (
                "id" uuid NOT NULL,
                "name" character varying({PermissionGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "display_name" character varying({PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "extra_properties" text NOT NULL,
                CONSTRAINT "pk_{groupsName}" PRIMARY KEY ("id")
            );

            CREATE TABLE IF NOT EXISTS {definitionsTable} (
                "id" uuid NOT NULL,
                "group_name" character varying({PermissionGroupDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "name" character varying({PermissionDefinitionRecordConstants.NameMaxLength}) NOT NULL,
                "display_name" character varying({PermissionDefinitionRecordConstants.DisplayNameMaxLength}) NOT NULL,
                "is_enabled" boolean NOT NULL,
                "parent_name" character varying({PermissionDefinitionRecordConstants.NameMaxLength}),
                "providers" character varying({PermissionDefinitionRecordConstants.ProvidersMaxLength}),
                "extra_properties" text NOT NULL,
                CONSTRAINT "pk_{definitionsName}" PRIMARY KEY ("id")
            );

            CREATE TABLE IF NOT EXISTS {grantsTable} (
                "id" uuid NOT NULL,
                "name" character varying({PermissionGrantRecordConstants.NameMaxLength}) NOT NULL,
                "provider_name" character varying({PermissionGrantRecordConstants.ProviderNameMaxLength}) NOT NULL,
                "provider_key" character varying({PermissionGrantRecordConstants.ProviderKeyMaxLength}) NOT NULL,
                "tenant_id" character varying({PermissionGrantRecordConstants.TenantIdMaxLength}),
                "is_granted" boolean NOT NULL DEFAULT TRUE,
                "created_at" timestamp with time zone NOT NULL,
                "updated_at" timestamp with time zone,
                CONSTRAINT "pk_{grantsName}" PRIMARY KEY ("id")
            );
            """;

        var indexesSql = $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{groupsName}_name" ON {groupsTable} ("name");
            CREATE INDEX IF NOT EXISTS "ix_{definitionsName}_group_name" ON {definitionsTable} ("group_name");
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{definitionsName}_name" ON {definitionsTable} ("name");
            CREATE INDEX IF NOT EXISTS "ix_{grantsName}_provider_name_provider_key" ON {grantsTable} ("provider_name", "provider_key");
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{grantsName}_tenant_id_name_provider_name_provider_key" ON {grantsTable} ("tenant_id", "name", "provider_name", "provider_key") WHERE "tenant_id" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_{grantsName}_name_provider_name_provider_key_no_tenant" ON {grantsTable} ("name", "provider_name", "provider_key") WHERE "tenant_id" IS NULL;
            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Permissions",
                (grantsName, _Defaults.GrantsName),
                (definitionsName, _Defaults.DefinitionsName),
                (groupsName, _Defaults.GroupsName)
            ),
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: () => PostgreSqlDialect.Instance.CreateConnection(options.ConnectionString),
            schema: tables.Schema,
            steps:
            [
                new SchemaStep(TablesStepVersion, "Create the grant, definition, and group tables.", tablesSql),
                new SchemaStep(IndexesStepVersion, "Create the grant, definition, and group indexes.", indexesSql),
            ],
            applyOnStartup: applyOnStartup,
            hostStateTables: [definitionsName, groupsName]
        );
    }
}
