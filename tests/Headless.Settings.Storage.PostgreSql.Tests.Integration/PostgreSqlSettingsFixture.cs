// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings;
using Headless.Testing.Testcontainers;
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

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("settings_storage_test").WithUsername("postgres").WithPassword("postgres");
    }
}
