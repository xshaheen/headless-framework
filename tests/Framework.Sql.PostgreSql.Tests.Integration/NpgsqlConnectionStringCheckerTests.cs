// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Sql.PostgreSql;
using Framework.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Tests.TestSetup;

namespace Tests;

[Collection<NpgsqlTestFixture>]
public sealed class NpgsqlConnectionStringCheckerTests(NpgsqlTestFixture fixture) : TestBase
{
    private static NpgsqlConnectionStringChecker GetSut()
    {
        return new NpgsqlConnectionStringChecker(NullLogger<NpgsqlConnectionStringChecker>.Instance);
    }

    [Fact]
    public async Task should_return_connected_true_when_server_reachable()
    {
        // given
        var sut = GetSut();
        var connectionString = fixture.Container.GetConnectionString();

        // when
        var (connected, _) = await sut.CheckAsync(connectionString);

        // then
        connected.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_database_exists_true_when_db_exists()
    {
        // given
        var sut = GetSut();
        var connectionString = fixture.Container.GetConnectionString();

        // when
        var (_, databaseExists) = await sut.CheckAsync(connectionString);

        // then
        databaseExists.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_connected_false_when_server_unreachable()
    {
        // given
        var sut = GetSut();
        var connectionString = "Host=10.255.255.1;Port=5432;Database=test;Username=test;Password=test;Timeout=1";

        // when
        var (connected, _) = await sut.CheckAsync(connectionString);

        // then
        connected.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_database_exists_false_when_db_missing()
    {
        // given
        var sut = GetSut();
        var builder = new NpgsqlConnectionStringBuilder(fixture.Container.GetConnectionString())
        {
            Database = "nonexistent_db_12345",
        };

        // when
        var (connected, databaseExists) = await sut.CheckAsync(builder.ConnectionString);

        // then
        connected.Should().BeTrue();
        databaseExists.Should().BeFalse();
    }

    [Fact]
    public async Task should_change_to_target_database()
    {
        // given
        var sut = GetSut();
        var connectionString = fixture.Container.GetConnectionString();

        // when - if change database succeeds, it returns true for both
        var (connected, databaseExists) = await sut.CheckAsync(connectionString);

        // then
        connected.Should().BeTrue();
        databaseExists.Should().BeTrue();
    }

    [Fact]
    public async Task should_close_connection_after_check()
    {
        // given
        var sut = GetSut();
        var connectionString = fixture.Container.GetConnectionString();

        // when
        await sut.CheckAsync(connectionString);

        // then - verify can establish new connection (no connection leak)
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(AbortToken);
        conn.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    public async Task should_handle_malformed_connection_string()
    {
        // given
        var sut = GetSut();
        var connectionString = "this is not a valid connection string";

        // when
        var (connected, databaseExists) = await sut.CheckAsync(connectionString);

        // then
        connected.Should().BeFalse();
        databaseExists.Should().BeFalse();
    }
}
