// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.PostgreSql;

public sealed class PostgreSqlSharedConnectionStringTests : TestBase
{
    [Fact]
    public void should_return_registered_connection_string_when_postgresql_factory_is_registered()
    {
        // given
        const string connectionString = "Host=localhost;Database=shared";
        var services = new ServiceCollection();
        services.AddPostgreSqlSql(connectionString);
        using var provider = services.BuildServiceProvider();

        // when
        var result = provider.GetPostgreSqlConnectionString();

        // then
        result.Should().Be(connectionString);
    }

    [Fact]
    public void should_throw_naming_add_postgresql_sql_when_no_factory_is_registered()
    {
        // given
        using var provider = new ServiceCollection().BuildServiceProvider();

        // when
        var act = () => provider.GetPostgreSqlConnectionString();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddPostgreSqlSql(connectionString)*");
    }

    [Fact]
    public void should_throw_naming_add_postgresql_sql_when_sql_server_factory_is_registered()
    {
        // given
        var services = new ServiceCollection();
        services.AddSqlServerSql("Server=localhost;Database=shared");
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetPostgreSqlConnectionString();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*SqlServerConnectionFactory*AddPostgreSqlSql(connectionString)*");
    }
}
