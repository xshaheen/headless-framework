// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Settings;
using Headless.Testing.Testcontainers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlSettingsFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlSettingsFixture>,
        ISettingsStorageFixture
{
    public string ConnectionString => Container.GetConnectionString();

    public Type ProviderExceptionType => typeof(PostgresException);

    public void UseStorage(HeadlessSettingsSetupBuilder setup, string connectionString)
    {
        setup.UsePostgreSql(connectionString);
    }

    public async Task DropSchemaAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TableExistsAsync(string schema, string tableName, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @table
            )
            """,
            connection
        );
        command.Parameters.AddWithValue(nameof(schema), schema);
        command.Parameters.AddWithValue("table", tableName);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public StorageNamingStyle NamingStyle => StorageNamingStyle.SnakeCase;

    public void UseEntityFrameworkProvider(DbContextOptionsBuilder builder, string connectionString)
    {
        builder.UseNpgsql(connectionString);
    }

    public async Task<SettingsStoreObjects> ReadStoreObjectsAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var columns = await _ReadPairsAsync(
            connection,
            "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = @schema",
            schema,
            cancellationToken
        );
        var indexes = await _ReadPairsAsync(
            connection,
            "SELECT tablename, indexname FROM pg_indexes WHERE schemaname = @schema",
            schema,
            cancellationToken
        );

        return new SettingsStoreObjects(columns, indexes);
    }

    private static async Task<HashSet<string>> _ReadPairsAsync(
        NpgsqlConnection connection,
        string sql,
        string schema,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(nameof(schema), schema);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var pairs = new HashSet<string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellationToken))
        {
            pairs.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }

        return pairs;
    }

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("settings_storage_test").WithUsername("postgres").WithPassword("postgres");
    }
}
