// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.PostgreSql;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

/// <summary>
/// The DistributedLocks feature's schema contribution for PostgreSQL: the fence sequence that
/// <see cref="PostgresFencingTokenSource"/> reads, as one idempotent step the Headless schema runner applies.
/// </summary>
internal static class PostgreSqlDistributedLocksSchemaContribution
{
    public const string StepVersion = "1";

    public const string SequenceName = "headless_distributed_locks_fence";

    // Quoted rather than bare: PostgreSQL case-folds unquoted identifiers, so a mixed-case schema would be created
    // under one name and read back under another.
    public static string QualifiedSequence(string schema)
    {
        return $"\"{schema}\".\"{SequenceName}\"";
    }

    public static SchemaContribution Create(DistributedLocksStorageOptions storageOptions, NpgsqlDataSource dataSource)
    {
        var schema = storageOptions.Schema;

        return new SchemaContribution(
            feature: "DistributedLocks",
            dialect: PostgreSqlSchemaDialect.Instance,
            // The provider's data source, consumer-supplied or built from the connection string, so the runner
            // reaches the same database the fencing-token source reads.
            createConnection: dataSource.CreateConnection,
            schema: schema,
            steps:
            [
                new SchemaStep(
                    StepVersion,
                    "Create the fence sequence.",
                    $"CREATE SEQUENCE IF NOT EXISTS {QualifiedSequence(schema)};"
                ),
            ]
        );
    }
}
