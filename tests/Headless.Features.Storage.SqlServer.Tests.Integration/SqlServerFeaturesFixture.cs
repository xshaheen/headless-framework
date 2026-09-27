// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerFeaturesFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerFeaturesFixture>,
        IFeaturesStorageFixture
{
    public Type ProviderExceptionType => typeof(SqlException);

    public void UseStorage(HeadlessFeaturesSetupBuilder setup, string connectionString)
    {
        setup.UseSqlServer(connectionString);
    }

    public async Task DropSchemaAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        // The table types go before the schema: SQL Server refuses to drop a schema that still owns objects.
        await using var command = new SqlCommand(
            $"""
            IF OBJECT_ID(N'{schema}.FeatureValues', N'U') IS NOT NULL DROP TABLE [{schema}].[FeatureValues];
            IF OBJECT_ID(N'{schema}.FeatureDefinitions', N'U') IS NOT NULL DROP TABLE [{schema}].[FeatureDefinitions];
            IF OBJECT_ID(N'{schema}.FeatureGroupDefinitions', N'U') IS NOT NULL DROP TABLE [{schema}].[FeatureGroupDefinitions];
            IF TYPE_ID(N'{schema}.HeadlessFeaturesIdList') IS NOT NULL DROP TYPE [{schema}].[HeadlessFeaturesIdList];
            IF TYPE_ID(N'{schema}.HeadlessFeaturesNameList') IS NOT NULL DROP TYPE [{schema}].[HeadlessFeaturesNameList];
            IF EXISTS (SELECT * FROM sys.schemas WHERE name = N'{schema}') EXEC(N'DROP SCHEMA [{schema}]');
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TableExistsAsync(string schema, string tableName, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @table
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
            """,
            connection
        );
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", tableName);

        return (bool)await command.ExecuteScalarAsync(cancellationToken);
    }
}
