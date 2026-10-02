// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.Sqlite;

namespace Headless.Sequences.Sqlite;

/// <summary>The Sequences feature's schema contribution for SQLite: the counter table, as one step.</summary>
internal static class SqliteSequencesSchemaContribution
{
    public const string StepVersion = "1";

    public static SchemaContribution Create(SqliteSequencesOptions options)
    {
        var dialect = SqliteDialect.Instance;
        var table = dialect.Qualify(options.Schema, options.TableName);
        var tenantId = SequencesColumns.TenantId(dialect);
        var name = SequencesColumns.Name(dialect);
        var partition = SequencesColumns.Partition(dialect);

        // TEXT compares with SQLite's BINARY collation, so counter names, partitions, and tenant ids match ordinally.
        // Instants are the dialect's fixed-width UTC text.
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {table} (
                {tenantId} TEXT NOT NULL,
                {name} TEXT NOT NULL,
                {partition} TEXT NOT NULL,
                {SequencesColumns.Value(dialect)} INTEGER NOT NULL,
                {SequencesColumns.CreatedAt(dialect)} TEXT NOT NULL,
                {SequencesColumns.UpdatedAt(dialect)} TEXT NOT NULL,
                CONSTRAINT {dialect.Quote("pk_" + options.TableName)} PRIMARY KEY ({tenantId}, {name}, {partition})
            );
            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Sequences",
                (options.TableName, SqliteSequencesOptions.DefaultTableName)
            ),
            dialect: SqliteSchemaDialect.Instance,
            createConnection: () => dialect.CreateConnection(options.ConnectionString),
            schema: options.Schema,
            steps: [new SchemaStep(StepVersion, "Create the counter table.", sql)],
            applyOnStartup: options.InitializeOnStartup
        );
    }
}
