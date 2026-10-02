// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.Sqlite;

namespace Headless.Coordination.Sqlite;

/// <summary>
/// The Coordination feature's schema contribution for SQLite: the membership generation, descriptor, and liveness
/// tables plus the liveness index, as one idempotent step the Headless schema runner applies.
/// </summary>
internal static class SqliteMembershipSchemaContribution
{
    public const string TablesStepVersion = "1";

    public static SchemaContribution Create(
        SqliteCoordinationOptions providerOptions,
        CoordinationStorageOptions storageOptions
    )
    {
        var dialect = SqliteDialect.Instance;
        var schema = storageOptions.Schema;
        var t = new CoordinationTables(dialect, schema);
        const string emptyJson = "'{}'";
        string pk(string table) => dialect.Quote("pk_" + table);

        // TEXT compares with SQLite's BINARY collation, so cluster names and node ids match ordinally, as PostgreSQL's
        // "C" and SQL Server's _BIN2 key columns do. Instants are the dialect's fixed-width UTC text, and JSON is text.
        // Index names are global to a SQLite file, so the index carries the schema prefix like the tables.
        var tablesSql = $"""
            CREATE TABLE IF NOT EXISTS {t.Generation} (
                {t.ClusterName} TEXT NOT NULL,
                {t.NodeId} TEXT NOT NULL,
                {t.CurrentIncarnation} INTEGER NOT NULL,
                {t.UpdatedAt} TEXT NOT NULL,
                CONSTRAINT {pk(t.GenerationTableName)} PRIMARY KEY ({t.ClusterName}, {t.NodeId})
            );

            CREATE TABLE IF NOT EXISTS {t.Descriptor} (
                {t.ClusterName} TEXT NOT NULL,
                {t.NodeId} TEXT NOT NULL,
                {t.Incarnation} INTEGER NOT NULL,
                {t.HostName} TEXT NULL,
                {t.Endpoints} TEXT NOT NULL DEFAULT {emptyJson},
                {t.Role} TEXT NULL,
                {t.Metadata} TEXT NOT NULL DEFAULT {emptyJson},
                {t.CreatedAt} TEXT NOT NULL,
                CONSTRAINT {pk(t.DescriptorTableName)} PRIMARY KEY ({t.ClusterName}, {t.NodeId}, {t.Incarnation})
            );

            CREATE TABLE IF NOT EXISTS {t.Liveness} (
                {t.ClusterName} TEXT NOT NULL,
                {t.NodeId} TEXT NOT NULL,
                {t.Incarnation} INTEGER NOT NULL,
                {t.LastBeat} TEXT NOT NULL,
                {t.LeftAt} TEXT NULL,
                CONSTRAINT {pk(t.LivenessTableName)} PRIMARY KEY ({t.ClusterName}, {t.NodeId}, {t.Incarnation})
            );

            CREATE INDEX IF NOT EXISTS {dialect.Quote(
                SqliteDialect.QualifiedName(schema, "ix_" + t.LivenessTableName + "_cluster_lastbeat")
            )}
                ON {t.Liveness} ({t.ClusterName}, {t.LastBeat});
            """;

        return new SchemaContribution(
            feature: "Coordination",
            dialect: SqliteSchemaDialect.Instance,
            createConnection: () => dialect.CreateConnection(providerOptions.ConnectionString),
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
