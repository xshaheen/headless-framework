// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.SqlServer;

namespace Headless.Sequences.SqlServer;

/// <summary>The Sequences feature's schema contribution for SQL Server: the counter table, as one step.</summary>
internal static class SqlServerSequencesSchemaContribution
{
    public const string StepVersion = "1";

    // Binary code-point order, so counter names, partitions, and tenant ids match case- and accent-sensitively whatever
    // the database's default collation is. SQL Server still pads trailing spaces before comparing; key parts with
    // surrounding white space are refused before any statement, which keeps matching ordinal on both engines.
    private const string _KeyCollation = "Latin1_General_100_BIN2";

    public static SchemaContribution Create(SqlServerSequencesOptions options)
    {
        var dialect = SqlServerDialect.Instance;
        var table = dialect.Qualify(options.Schema, options.TableName);
        var objectName = $"{options.Schema}.{options.TableName}";
        var tenantId = SequencesColumns.TenantId(dialect);
        var name = SequencesColumns.Name(dialect);
        var partition = SequencesColumns.Partition(dialect);

        // The clustered primary key over exactly (TenantId, Name, Partition) is load-bearing, not an index choice:
        // the upsert's HOLDLOCK takes its key-range lock on this index, which is what serializes concurrent first
        // calls on a new key. 128 + 128 + 64 nvarchar characters stay under the 900-byte clustered key limit.
        var sql = $"""
            IF OBJECT_ID(N'{objectName}', N'U') IS NULL
                CREATE TABLE {table} (
                    {tenantId} nvarchar({SequenceFieldLimits.TenantIdMaxLength}) COLLATE {_KeyCollation} NOT NULL,
                    {name} nvarchar({SequenceFieldLimits.NameMaxLength}) COLLATE {_KeyCollation} NOT NULL,
                    {partition} nvarchar({SequenceFieldLimits.PartitionMaxLength}) COLLATE {_KeyCollation} NOT NULL,
                    {SequencesColumns.Value(dialect)} bigint NOT NULL,
                    {SequencesColumns.CreatedAt(dialect)} datetimeoffset(7) NOT NULL,
                    {SequencesColumns.UpdatedAt(dialect)} datetimeoffset(7) NOT NULL,
                    CONSTRAINT {dialect.Quote(
                "PK_" + options.TableName
            )} PRIMARY KEY CLUSTERED ({tenantId} ASC, {name} ASC, {partition} ASC)
                );
            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Sequences",
                (options.TableName, SqlServerSequencesOptions.DefaultTableName)
            ),
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: () => dialect.CreateConnection(options.ConnectionString),
            schema: options.Schema,
            steps: [new SchemaStep(StepVersion, "Create the counter table.", sql)],
            applyOnStartup: options.InitializeOnStartup
        );
    }
}
