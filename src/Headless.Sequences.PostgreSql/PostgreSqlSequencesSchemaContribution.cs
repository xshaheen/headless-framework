// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.PostgreSql;

namespace Headless.Sequences.PostgreSql;

/// <summary>The Sequences feature's schema contribution for PostgreSQL: the counter table, as one step.</summary>
internal static class PostgreSqlSequencesSchemaContribution
{
    public const string StepVersion = "1";

    public static SchemaContribution Create(PostgreSqlSequencesOptions options)
    {
        var dialect = PostgreSqlDialect.Instance;
        var table = dialect.Qualify(options.Schema, options.TableName);
        var tenantId = SequencesColumns.TenantId(dialect);
        var name = SequencesColumns.Name(dialect);
        var partition = SequencesColumns.Partition(dialect);

        // Key columns compare with the "C" collation, so counter names, partitions, and tenant ids match ordinally
        // (byte for byte) whatever the database's default collation is.
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {table} (
                {tenantId} varchar({SequenceFieldLimits.TenantIdMaxLength}) COLLATE "C" NOT NULL,
                {name} varchar({SequenceFieldLimits.NameMaxLength}) COLLATE "C" NOT NULL,
                {partition} varchar({SequenceFieldLimits.PartitionMaxLength}) COLLATE "C" NOT NULL,
                {SequencesColumns.Value(dialect)} bigint NOT NULL,
                {SequencesColumns.CreatedAt(dialect)} timestamptz NOT NULL,
                {SequencesColumns.UpdatedAt(dialect)} timestamptz NOT NULL,
                CONSTRAINT {dialect.Quote("pk_" + options.TableName)} PRIMARY KEY ({tenantId}, {name}, {partition})
            );
            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Sequences",
                (options.TableName, PostgreSqlSequencesOptions.DefaultTableName)
            ),
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: () => dialect.CreateConnection(options.ConnectionString),
            schema: options.Schema,
            steps: [new SchemaStep(StepVersion, "Create the counter table.", sql)],
            applyOnStartup: options.InitializeOnStartup
        );
    }
}
