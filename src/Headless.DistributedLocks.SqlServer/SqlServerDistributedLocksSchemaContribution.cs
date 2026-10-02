// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.SqlServer;

namespace Headless.DistributedLocks.SqlServer;

/// <summary>
/// The DistributedLocks feature's schema contribution for SQL Server: the fence sequence that
/// <see cref="SqlServerFencingTokenSource"/> reads, as one idempotent step the Headless schema runner applies.
/// </summary>
internal static class SqlServerDistributedLocksSchemaContribution
{
    public const string StepVersion = "1";

    public static SchemaContribution Create(
        SqlServerDistributedLockOptions options,
        DistributedLocksStorageOptions storageOptions
    )
    {
        var schema = storageOptions.Schema;
        var sequenceName = SqlServerIdentifier.FenceSequenceName(options.KeyPrefix);
        var qualifiedSequence = $"{SqlServerIdentifier.Quote(schema)}.{SqlServerIdentifier.Quote(sequenceName)}";

        // Both names are safe inside the N'' literals: the schema passed SQL Server's regular-identifier validation at
        // startup, and the sequence name keeps only [A-Za-z0-9_].
        var sql = $"""
            IF NOT EXISTS (
                SELECT 1
                FROM sys.sequences s
                JOIN sys.schemas sc ON sc.schema_id = s.schema_id
                WHERE s.name = N'{sequenceName}' AND sc.name = N'{schema}'
            )
                CREATE SEQUENCE {qualifiedSequence} AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;
            """;

        return new SchemaContribution(
            // Each key prefix owns its own sequence, so hosts with different prefixes in one schema must keep separate
            // history rows; under one identity the second host would never create its sequence.
            feature: SchemaContribution.FeatureId(
                "DistributedLocks",
                (sequenceName, SqlServerIdentifier.FenceSequenceName(DistributedLockOptions.DefaultKeyPrefix))
            ),
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: options.CreateConnection,
            schema: schema,
            steps: [new SchemaStep(StepVersion, "Create the fence sequence.", sql)],
            applyOnStartup: options.EnableFencing
        );
    }
}
