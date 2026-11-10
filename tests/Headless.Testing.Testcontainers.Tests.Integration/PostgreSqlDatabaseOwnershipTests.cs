// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Npgsql;

namespace Tests;

[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlOwnershipFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlOwnershipFixture>;

[Collection<PostgreSqlOwnershipFixture>]
public sealed class PostgreSqlDatabaseOwnershipTests(PostgreSqlOwnershipFixture fixture) : TestBase
{
    [Fact]
    public async Task should_connect_to_a_database_of_the_fixtures_own()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand("SELECT current_database();", connection);

        (await command.ExecuteScalarAsync(AbortToken)).Should().Be(fixture.DatabaseName);
        fixture.DatabaseName.Should().MatchRegex("^hf_[0-9a-f]{12}$");
    }

    [Fact]
    public async Task should_drop_every_owned_database_on_dispose_and_keep_the_others()
    {
        var other = await PostgreSqlDatabaseOwnership.CreateAsync(fixture.AdminConnectionString, AbortToken);
        var derived = TestDatabaseOwner.Derived(other.OwnerId, "extra");
        await other.CreateDatabaseAsync(derived, AbortToken);

        // A session left open in the database must not stop the drop.
        await using var lingering = new NpgsqlConnection(fixture.ConnectionStringFor(derived));
        await lingering.OpenAsync(AbortToken);

        await other.DisposeAsync();

        var databases = await _DatabasesAsync();
        databases.Should().NotContain([other.OwnerId, derived]);
        databases.Should().Contain(fixture.DatabaseName);
    }

    [Fact]
    public async Task should_sweep_the_databases_of_an_owner_whose_process_is_gone()
    {
        // A database with the owner naming but no session holding its lock is what a killed run leaves behind.
        var orphan = TestDatabaseOwner.NewId();
        await _ExecuteAsync($"CREATE DATABASE \"{orphan}\";");
        await _ExecuteAsync($"CREATE DATABASE \"{orphan}_extra\";");

        var next = await PostgreSqlDatabaseOwnership.CreateAsync(fixture.AdminConnectionString, AbortToken);
        await next.DisposeAsync();

        var databases = await _DatabasesAsync();
        databases.Should().NotContain([orphan, orphan + "_extra"]);
        databases.Should().Contain(fixture.DatabaseName, "a live owner keeps its databases through another's sweep");
    }

    private async Task<List<string>> _DatabasesAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand("SELECT datname FROM pg_database;", connection);
        await using var reader = await command.ExecuteReaderAsync(AbortToken);

        var names = new List<string>();

        while (await reader.ReadAsync(AbortToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task _ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(AbortToken);
#pragma warning disable CA2100 // False positive: the statement names only a database this test generated.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(AbortToken);
    }
}
