// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Schema;
using Headless.Sql.PostgreSql;

namespace Headless.Search.PostgreSql;

/// <summary>The Arabic search schema contribution for PostgreSQL: one step per rule version, each creating its function.</summary>
internal static class PostgreSqlArabicSearchSchemaContribution
{
    public const string Feature = "ArabicSearch";

    public static SchemaContribution Create(PostgreSqlSearchOptions options)
    {
        // No SchemaObject is declared: dropping a function would cascade into the application's generated columns,
        // so purge and delete stay unsupported for this contribution.
        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(Feature),
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: () => PostgreSqlDialect.Instance.CreateConnection(options.ConnectionString),
            schema: options.Schema,
            steps:
            [
                new SchemaStep(
                    "1",
                    "Create ar_norm_v1.",
                    PostgreSqlArabicSearchFunction.CreateSql(options.Schema, ArabicSearchVersion.V1)
                ),
            ]
        );
    }
}
