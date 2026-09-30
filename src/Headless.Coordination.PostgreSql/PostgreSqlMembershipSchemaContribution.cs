// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Hosting.Initialization.Schema;
using Headless.Sql.PostgreSql;

namespace Headless.Coordination.PostgreSql;

/// <summary>
/// The Coordination feature's schema contribution for PostgreSQL: the membership generation, descriptor, and
/// liveness tables plus the liveness index, and the legacy column renames, as two idempotent steps the Headless
/// schema runner applies.
/// </summary>
internal static class PostgreSqlMembershipSchemaContribution
{
    public const string TablesStepVersion = "1";
    public const string RenameStepVersion = "2";

    public static SchemaContribution Create(
        PostgreSqlCoordinationOptions providerOptions,
        CoordinationStorageOptions storageOptions
    )
    {
        var schema = storageOptions.Schema;
        var generationTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Generation.Table);
        var descriptorTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Descriptor.Table);
        var livenessTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Liveness.Table);

        var tablesSql = $$"""
            CREATE TABLE IF NOT EXISTS {{generationTable}} (
                {{PostgreSqlMembershipSchema.ClusterName}} varchar(200) NOT NULL,
                {{PostgreSqlMembershipSchema.NodeId}} varchar(400) NOT NULL,
                {{PostgreSqlMembershipSchema.Generation.CurrentIncarnation}} bigint NOT NULL,
                {{PostgreSqlMembershipSchema.UpdatedAt}} timestamptz NOT NULL,
                CONSTRAINT pk_{{PostgreSqlMembershipSchema.Generation.Table}} PRIMARY KEY (
                    {{PostgreSqlMembershipSchema.ClusterName}},
                    {{PostgreSqlMembershipSchema.NodeId}}
                )
            );

            CREATE TABLE IF NOT EXISTS {{descriptorTable}} (
                {{PostgreSqlMembershipSchema.ClusterName}} varchar(200) NOT NULL,
                {{PostgreSqlMembershipSchema.NodeId}} varchar(400) NOT NULL,
                {{PostgreSqlMembershipSchema.Incarnation}} bigint NOT NULL,
                {{PostgreSqlMembershipSchema.Descriptor.HostName}} text NULL,
                {{PostgreSqlMembershipSchema.Descriptor.Endpoints}} jsonb NOT NULL DEFAULT '{}'::jsonb,
                {{PostgreSqlMembershipSchema.Descriptor.Role}} varchar(200) NULL,
                {{PostgreSqlMembershipSchema.Descriptor.Metadata}} jsonb NOT NULL DEFAULT '{}'::jsonb,
                {{PostgreSqlMembershipSchema.CreatedAt}} timestamptz NOT NULL,
                CONSTRAINT pk_{{PostgreSqlMembershipSchema.Descriptor.Table}} PRIMARY KEY (
                    {{PostgreSqlMembershipSchema.ClusterName}},
                    {{PostgreSqlMembershipSchema.NodeId}},
                    {{PostgreSqlMembershipSchema.Incarnation}}
                )
            );

            CREATE TABLE IF NOT EXISTS {{livenessTable}} (
                {{PostgreSqlMembershipSchema.ClusterName}} varchar(200) NOT NULL,
                {{PostgreSqlMembershipSchema.NodeId}} varchar(400) NOT NULL,
                {{PostgreSqlMembershipSchema.Incarnation}} bigint NOT NULL,
                {{PostgreSqlMembershipSchema.Liveness.LastBeat}} timestamptz NOT NULL,
                {{PostgreSqlMembershipSchema.Liveness.LeftAt}} timestamptz NULL,
                CONSTRAINT pk_{{PostgreSqlMembershipSchema.Liveness.Table}} PRIMARY KEY (
                    {{PostgreSqlMembershipSchema.ClusterName}},
                    {{PostgreSqlMembershipSchema.NodeId}},
                    {{PostgreSqlMembershipSchema.Incarnation}}
                )
            );

            CREATE INDEX IF NOT EXISTS ix_{{PostgreSqlMembershipSchema.Liveness.Table}}_cluster_lastbeat
                ON {{livenessTable}} ({{PostgreSqlMembershipSchema.ClusterName}}, {{PostgreSqlMembershipSchema.Liveness.LastBeat}});
            """;

        // The renames are a separate step: they are a schema repair, not a creation, and keeping them apart keeps
        // the creation step's checksum stable while the repair evolves.
        var renameSql = $$"""
            DO $migration$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM pg_attribute
                    WHERE attrelid = to_regclass('{{generationTable}}')
                      AND attname = 'date_updated'
                      AND NOT attisdropped
                ) AND NOT EXISTS (
                    SELECT 1 FROM pg_attribute
                    WHERE attrelid = to_regclass('{{generationTable}}')
                      AND attname = '{{PostgreSqlMembershipSchema.UpdatedAt}}'
                      AND NOT attisdropped
                ) THEN
                    ALTER TABLE {{generationTable}}
                        RENAME COLUMN date_updated TO {{PostgreSqlMembershipSchema.UpdatedAt}};
                END IF;

                IF EXISTS (
                    SELECT 1 FROM pg_attribute
                    WHERE attrelid = to_regclass('{{descriptorTable}}')
                      AND attname = 'date_created'
                      AND NOT attisdropped
                ) AND NOT EXISTS (
                    SELECT 1 FROM pg_attribute
                    WHERE attrelid = to_regclass('{{descriptorTable}}')
                      AND attname = '{{PostgreSqlMembershipSchema.CreatedAt}}'
                      AND NOT attisdropped
                ) THEN
                    ALTER TABLE {{descriptorTable}}
                        RENAME COLUMN date_created TO {{PostgreSqlMembershipSchema.CreatedAt}};
                END IF;
            END $migration$;
            """;

        return new SchemaContribution(
            feature: "Coordination",
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: providerOptions.CreateConnection,
            schema: schema,
            steps:
            [
                new SchemaStep(
                    TablesStepVersion,
                    "Create the membership generation, descriptor, and liveness tables.",
                    tablesSql
                ),
                new SchemaStep(
                    RenameStepVersion,
                    "Rename the legacy date_updated and date_created columns to the current names.",
                    renameSql
                ),
            ],
            applyOnStartup: providerOptions.InitializeOnStartup
        );
    }
}
