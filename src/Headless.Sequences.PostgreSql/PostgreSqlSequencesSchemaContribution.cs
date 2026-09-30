// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.PostgreSql;

namespace Headless.Sequences.PostgreSql;

/// <summary>
/// The Sequences feature's schema contribution for PostgreSQL: the counter table, as one idempotent step the Headless
/// schema runner applies.
/// </summary>
internal static class PostgreSqlSequencesSchemaContribution
{
    public const string StepVersion = "1";

    public static SchemaContribution Create(PostgreSqlSequencesOptions options)
    {
        var table = PostgreSqlSequencesSchema.Qualified(options);

        // Key columns compare with the "C" collation, so counter names, partitions, and tenant ids match ordinally
        // (byte for byte) whatever the database's default collation is.
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {table} (
                {PostgreSqlSequencesSchema.TenantId} varchar({SequenceFieldLimits.TenantIdMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlSequencesSchema.Name} varchar({SequenceFieldLimits.NameMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlSequencesSchema.Partition} varchar({SequenceFieldLimits.PartitionMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlSequencesSchema.Value} bigint NOT NULL,
                {PostgreSqlSequencesSchema.CreatedAt} timestamptz NOT NULL,
                {PostgreSqlSequencesSchema.UpdatedAt} timestamptz NOT NULL,
                CONSTRAINT "pk_{options.TableName}" PRIMARY KEY (
                    {PostgreSqlSequencesSchema.TenantId},
                    {PostgreSqlSequencesSchema.Name},
                    {PostgreSqlSequencesSchema.Partition}
                )
            );
            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Sequences",
                (options.TableName, PostgreSqlSequencesOptions.DefaultTableName)
            ),
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: options.CreateConnection,
            schema: options.Schema,
            steps: [new SchemaStep(StepVersion, "Create the counter table.", sql)],
            applyOnStartup: options.InitializeOnStartup
        );
    }
}
