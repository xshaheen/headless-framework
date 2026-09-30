// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Hosting.Initialization.Schema;
using Headless.Sql.PostgreSql;

namespace Headless.Coordination.PostgreSql;

/// <summary>
/// The Coordination feature's schema contribution for PostgreSQL: the membership generation, descriptor, and
/// liveness tables plus the liveness index, as one idempotent step the Headless
/// schema runner applies.
/// </summary>
internal static class PostgreSqlMembershipSchemaContribution
{
    public const string TablesStepVersion = "1";

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
            ],
            applyOnStartup: providerOptions.InitializeOnStartup
        );
    }
}
