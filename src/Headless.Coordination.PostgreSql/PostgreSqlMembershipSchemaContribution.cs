// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.PostgreSql;

namespace Headless.Coordination.PostgreSql;

/// <summary>
/// The Coordination feature's schema contribution for PostgreSQL: the membership generation, descriptor, and
/// liveness tables plus the liveness index, as one idempotent step the Headless schema runner applies.
/// </summary>
internal static class PostgreSqlMembershipSchemaContribution
{
    public const string TablesStepVersion = "1";

    public static SchemaContribution Create(
        PostgreSqlCoordinationOptions providerOptions,
        CoordinationStorageOptions storageOptions
    )
    {
        var dialect = PostgreSqlDialect.Instance;
        var t = new CoordinationTables(dialect, storageOptions.Schema);
        const string emptyJson = "'{}'::jsonb";
        string pk(string table) => dialect.Quote("pk_" + table);

        // Key columns compare with the "C" collation, so cluster names and node ids match ordinally (byte for byte),
        // as SQL Server's _BIN2 key columns do, whatever the database's default collation is.
        var tablesSql = $"""
            CREATE TABLE IF NOT EXISTS {t.Generation} (
                {t.ClusterName} varchar({CoordinationTables.ClusterNameMaxLength}) COLLATE "C" NOT NULL,
                {t.NodeId} varchar({CoordinationTables.NodeIdMaxLength}) COLLATE "C" NOT NULL,
                {t.CurrentIncarnation} bigint NOT NULL,
                {t.UpdatedAt} timestamptz NOT NULL,
                CONSTRAINT {pk(t.GenerationTableName)} PRIMARY KEY ({t.ClusterName}, {t.NodeId})
            );

            CREATE TABLE IF NOT EXISTS {t.Descriptor} (
                {t.ClusterName} varchar({CoordinationTables.ClusterNameMaxLength}) COLLATE "C" NOT NULL,
                {t.NodeId} varchar({CoordinationTables.NodeIdMaxLength}) COLLATE "C" NOT NULL,
                {t.Incarnation} bigint NOT NULL,
                {t.HostName} text NULL,
                {t.Endpoints} jsonb NOT NULL DEFAULT {emptyJson},
                {t.Role} varchar({CoordinationTables.RoleMaxLength}) NULL,
                {t.Metadata} jsonb NOT NULL DEFAULT {emptyJson},
                {t.CreatedAt} timestamptz NOT NULL,
                CONSTRAINT {pk(t.DescriptorTableName)} PRIMARY KEY ({t.ClusterName}, {t.NodeId}, {t.Incarnation})
            );

            CREATE TABLE IF NOT EXISTS {t.Liveness} (
                {t.ClusterName} varchar({CoordinationTables.ClusterNameMaxLength}) COLLATE "C" NOT NULL,
                {t.NodeId} varchar({CoordinationTables.NodeIdMaxLength}) COLLATE "C" NOT NULL,
                {t.Incarnation} bigint NOT NULL,
                {t.LastBeat} timestamptz NOT NULL,
                {t.LeftAt} timestamptz NULL,
                CONSTRAINT {pk(t.LivenessTableName)} PRIMARY KEY ({t.ClusterName}, {t.NodeId}, {t.Incarnation})
            );

            CREATE INDEX IF NOT EXISTS {dialect.Quote("ix_" + t.LivenessTableName + "_cluster_lastbeat")}
                ON {t.Liveness} ({t.ClusterName}, {t.LastBeat});
            """;

        return new SchemaContribution(
            feature: "Coordination",
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: providerOptions.CreateConnection,
            schema: storageOptions.Schema,
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
