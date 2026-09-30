// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.SqlServer;

public sealed class SqlServerSharedConnectionStringTests : TestBase
{
    [Fact]
    public void should_return_registered_connection_string_when_sql_server_factory_is_registered()
    {
        // given
        const string connectionString = "Server=localhost;Database=shared";
        var services = new ServiceCollection();
        services.AddSqlServerSql(connectionString);
        using var provider = services.BuildServiceProvider();

        // when
        var result = provider.GetSqlServerConnectionString();

        // then
        result.Should().Be(connectionString);
    }

    [Fact]
    public void should_throw_naming_add_sql_server_sql_when_no_factory_is_registered()
    {
        // given
        using var provider = new ServiceCollection().BuildServiceProvider();

        // when
        var act = () => provider.GetSqlServerConnectionString();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddSqlServerSql(connectionString)*");
    }

    [Fact]
    public void should_throw_naming_add_sql_server_sql_when_postgresql_factory_is_registered()
    {
        // given
        var services = new ServiceCollection();
        services.AddPostgreSqlSql("Host=localhost;Database=shared");
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetSqlServerConnectionString();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*NpgsqlConnectionFactory*AddSqlServerSql(connectionString)*");
    }
}
