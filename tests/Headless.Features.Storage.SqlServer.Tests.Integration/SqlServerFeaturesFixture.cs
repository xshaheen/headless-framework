// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Hosting.Initialization;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

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
        // The table types go before the schema: SQL Server refuses to drop a schema that still owns objects. The schema
        // runner's history goes too: the runner trusts it, so a history that outlived the tables would stop them being
        // recreated.
        await using var command = new SqlCommand(
            $"""
            IF OBJECT_ID(N'{schema}.FeatureValues', N'U') IS NOT NULL DROP TABLE [{schema}].[FeatureValues];
            IF OBJECT_ID(N'{schema}.FeatureDefinitions', N'U') IS NOT NULL DROP TABLE [{schema}].[FeatureDefinitions];
            IF OBJECT_ID(N'{schema}.FeatureGroupDefinitions', N'U') IS NOT NULL DROP TABLE [{schema}].[FeatureGroupDefinitions];
            IF OBJECT_ID(N'{schema}.headless_schema_history', N'U') IS NOT NULL DROP TABLE [{schema}].[headless_schema_history];
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

    public StorageNamingStyle NamingStyle => StorageNamingStyle.PascalCase;

    public void UseEntityFrameworkProvider(DbContextOptionsBuilder builder, string connectionString)
    {
        builder.UseSqlServer(connectionString);
    }

    public async Task<FeaturesStoreObjects> ReadStoreObjectsAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var columns = await _ReadPairsAsync(
            connection,
            "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @schema",
            schema,
            cancellationToken
        );
        var indexes = await _ReadPairsAsync(
            connection,
            """
            SELECT t.name, i.name
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema AND i.name IS NOT NULL
            """,
            schema,
            cancellationToken
        );

        return new FeaturesStoreObjects(columns, indexes);
    }

    private static async Task<HashSet<string>> _ReadPairsAsync(
        SqlConnection connection,
        string sql,
        string schema,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", schema);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var pairs = new HashSet<string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellationToken))
        {
            pairs.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }

        return pairs;
    }
}
