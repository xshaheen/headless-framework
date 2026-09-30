// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.SqlServer;

namespace Headless.Sequences.SqlServer;

/// <summary>
/// The Sequences feature's schema contribution for SQL Server: the counter table, as one idempotent step the Headless
/// schema runner applies.
/// </summary>
internal static class SqlServerSequencesSchemaContribution
{
    public const string StepVersion = "1";

    public static SchemaContribution Create(SqlServerSequencesOptions options)
    {
        var table = SqlServerSequencesSchema.Qualified(options);
        var objectName = $"{options.Schema}.{options.TableName}";
        const string collation = SqlServerSequencesSchema.KeyCollation;

        // The clustered primary key over exactly (TenantId, Name, Partition) is load-bearing, not an index choice:
        // the increment's HOLDLOCK takes its key-range lock on this index, which is what serializes concurrent first
        // calls on a new key. 128 + 128 + 64 nvarchar characters stay under the 900-byte clustered key limit.
        var sql = $"""
            IF OBJECT_ID(N'{objectName}', N'U') IS NULL
                CREATE TABLE {table} (
                    {SqlServerSequencesSchema.TenantId} nvarchar({SequenceFieldLimits.TenantIdMaxLength}) COLLATE {collation} NOT NULL,
                    {SqlServerSequencesSchema.Name} nvarchar({SequenceFieldLimits.NameMaxLength}) COLLATE {collation} NOT NULL,
                    {SqlServerSequencesSchema.Partition} nvarchar({SequenceFieldLimits.PartitionMaxLength}) COLLATE {collation} NOT NULL,
                    {SqlServerSequencesSchema.Value} bigint NOT NULL,
                    {SqlServerSequencesSchema.CreatedAt} datetime2 NOT NULL,
                    {SqlServerSequencesSchema.UpdatedAt} datetime2 NOT NULL,
                    CONSTRAINT [PK_{options.TableName}] PRIMARY KEY CLUSTERED (
                        {SqlServerSequencesSchema.TenantId} ASC,
                        {SqlServerSequencesSchema.Name} ASC,
                        {SqlServerSequencesSchema.Partition} ASC
                    )
                );
            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Sequences",
                (options.TableName, SqlServerSequencesOptions.DefaultTableName)
            ),
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: options.CreateConnection,
            schema: options.Schema,
            steps: [new SchemaStep(StepVersion, "Create the counter table.", sql)],
            applyOnStartup: options.InitializeOnStartup
        );
    }
}
