// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.SqlServer;

namespace Headless.Coordination.SqlServer;

/// <summary>
/// The Coordination feature's schema contribution for SQL Server: the membership generation, descriptor, and
/// liveness tables plus the liveness index, as one idempotent step the Headless schema runner applies.
/// </summary>
internal static class SqlServerMembershipSchemaContribution
{
    public const string TablesStepVersion = "1";

    // Ordinal comparison and ordering of key text, matching PostgreSQL's "C" key columns.
    private const string _KeyCollation = "Latin1_General_100_BIN2";

    public static SchemaContribution Create(
        SqlServerCoordinationOptions providerOptions,
        CoordinationStorageOptions storageOptions
    )
    {
        var dialect = SqlServerDialect.Instance;
        var schema = storageOptions.Schema;
        var t = new CoordinationTables(dialect, schema);
        var clusterName =
            $"{t.ClusterName} nvarchar({CoordinationTables.ClusterNameMaxLength}) COLLATE {_KeyCollation} NOT NULL";
        var nodeId = $"{t.NodeId} nvarchar({CoordinationTables.NodeIdMaxLength}) COLLATE {_KeyCollation} NOT NULL";
        string objectName(string table) => $"{schema}.{table}";
        string constraint(string prefix, string table) => dialect.Quote($"{prefix}_{table}");
        const string emptyJson = "N'{}'";
        var endpointsDefault = constraint("DF", t.DescriptorTableName + "_Endpoints");
        var metadataDefault = constraint("DF", t.DescriptorTableName + "_Metadata");

        // The clustered primary keys are load-bearing, not an index choice: the store's HOLDLOCK reads take their
        // key-range locks on them, which is what serializes two first writers of one key. Every instant is a
        // datetimeoffset(7), the type the dialect's clock and parameters carry.
        var tablesSql = $"""
            IF OBJECT_ID(N'{objectName(t.GenerationTableName)}', N'U') IS NULL
                CREATE TABLE {t.Generation} (
                    {clusterName},
                    {nodeId},
                    {t.CurrentIncarnation} bigint NOT NULL,
                    {t.UpdatedAt} datetimeoffset(7) NOT NULL,
                    CONSTRAINT {constraint("PK", t.GenerationTableName)} PRIMARY KEY CLUSTERED (
                        {t.ClusterName} ASC,
                        {t.NodeId} ASC
                    )
                );

            IF OBJECT_ID(N'{objectName(t.DescriptorTableName)}', N'U') IS NULL
                CREATE TABLE {t.Descriptor} (
                    {clusterName},
                    {nodeId},
                    {t.Incarnation} bigint NOT NULL,
                    {t.HostName} nvarchar(max) NULL,
                    {t.Endpoints} nvarchar(max) NOT NULL CONSTRAINT {endpointsDefault} DEFAULT {emptyJson},
                    {t.Role} nvarchar({CoordinationTables.RoleMaxLength}) NULL,
                    {t.Metadata} nvarchar(max) NOT NULL CONSTRAINT {metadataDefault} DEFAULT {emptyJson},
                    {t.CreatedAt} datetimeoffset(7) NOT NULL,
                    CONSTRAINT {constraint("PK", t.DescriptorTableName)} PRIMARY KEY CLUSTERED (
                        {t.ClusterName} ASC,
                        {t.NodeId} ASC,
                        {t.Incarnation} ASC
                    )
                );

            IF OBJECT_ID(N'{objectName(t.LivenessTableName)}', N'U') IS NULL
                CREATE TABLE {t.Liveness} (
                    {clusterName},
                    {nodeId},
                    {t.Incarnation} bigint NOT NULL,
                    {t.LastBeat} datetimeoffset(7) NOT NULL,
                    {t.LeftAt} datetimeoffset(7) NULL,
                    CONSTRAINT {constraint("PK", t.LivenessTableName)} PRIMARY KEY CLUSTERED (
                        {t.ClusterName} ASC,
                        {t.NodeId} ASC,
                        {t.Incarnation} ASC
                    )
                );

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_{t.LivenessTableName}_ClusterName_LastBeat'
                  AND object_id = OBJECT_ID(N'{objectName(t.LivenessTableName)}')
            )
                CREATE NONCLUSTERED INDEX {constraint("IX", t.LivenessTableName + "_ClusterName_LastBeat")}
                    ON {t.Liveness} ({t.ClusterName} ASC, {t.LastBeat} ASC);
            """;

        return new SchemaContribution(
            feature: "Coordination",
            dialect: SqlServerSchemaDialect.Instance,
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
