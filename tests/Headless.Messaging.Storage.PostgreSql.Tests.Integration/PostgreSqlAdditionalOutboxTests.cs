// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlAdditionalOutboxTests(PostgreSqlTestFixture fixture) : AdditionalOutboxConformanceTests
{
    protected override async Task<string> CreateDatabaseAsync(string name)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync($"CREATE DATABASE \"{name}\";");

        return new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name }.ConnectionString;
    }

    protected override async Task DropDatabaseAsync(string name)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);");
    }

    protected override void UseDatabase(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString);

    protected override void UsePrimaryStorage<TContext>(MessagingSetupBuilder setup) =>
        setup.UseEntityFramework<TContext>();

    protected override MessagingSetupBuilder UseOutboxStorage<TContext>(OutboxStorageBuilder outbox) =>
        outbox.UseEntityFramework<TContext>();

    protected override MessagingSetupBuilder UseOutboxStorage(OutboxStorageBuilder outbox, string connectionString) =>
        outbox.UsePostgreSql(connectionString);

    protected override async Task<IReadOnlyList<PublishedRow>> ReadPublishedAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        var rows = await connection.QueryAsync<(string? Content, string StatusName)>(
            "SELECT \"Content\", \"StatusName\" FROM messaging.\"published\";"
        );

        return rows.Select(row => new PublishedRow(row.Content, row.StatusName)).ToList();
    }

    protected override async Task<bool> HasReceivedTableAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        return await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('messaging.\"received\"') IS NOT NULL;");
    }
}
