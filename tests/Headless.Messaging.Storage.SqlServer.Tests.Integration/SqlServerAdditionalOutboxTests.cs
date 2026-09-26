// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerAdditionalOutboxTests(SqlServerTestFixture fixture) : AdditionalOutboxConformanceTests
{
    protected override async Task<string> CreateDatabaseAsync(string name)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync($"CREATE DATABASE [{name}];");

        return new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = name }.ConnectionString;
    }

    protected override async Task DropDatabaseAsync(string name)
    {
        // Pooled connections of this process still point at the database; drop them before the server drops it.
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            $"""
            IF DB_ID(N'{name}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{name}];
            END;
            """
        );
    }

    protected override void UseDatabase(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString);

    protected override void UsePrimaryStorage<TContext>(MessagingSetupBuilder setup) =>
        setup.UseEntityFramework<TContext>();

    protected override MessagingSetupBuilder UseOutboxStorage<TContext>(OutboxStorageBuilder outbox) =>
        outbox.UseEntityFramework<TContext>();

    protected override MessagingSetupBuilder UseOutboxStorage(OutboxStorageBuilder outbox, string connectionString) =>
        outbox.UseSqlServer(connectionString);

    protected override async Task<IReadOnlyList<PublishedRow>> ReadPublishedAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<(string? Content, string StatusName)>(
            "SELECT [Content], [StatusName] FROM [messaging].[Published];"
        );

        return rows.Select(row => new PublishedRow(row.Content, row.StatusName)).ToList();
    }

    protected override async Task<bool> HasReceivedTableAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);

        return await connection.ExecuteScalarAsync<bool>(
            "SELECT CAST(CASE WHEN OBJECT_ID(N'messaging.Received', N'U') IS NULL THEN 0 ELSE 1 END AS bit);"
        );
    }
}
