// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Fencing.SqlServer;
using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Resolves the provider options without a database, so the class stays outside the container collection.
/// </summary>
public sealed class SqlServerFencingSharedConnectionTests : TestBase
{
    [Fact]
    public async Task should_use_the_shared_connection_string_when_using_the_parameterless_overload()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqlServerSql("Server=localhost;Database=shared");
        services.AddHeadlessFencing(setup => setup.UseSqlServer());

        await using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<SqlServerFencingOptions>>().Value;

        // then
        options.ConnectionString.Should().Be("Server=localhost;Database=shared");
    }

    [Fact]
    public async Task should_prefer_its_own_connection_string_over_the_shared_connection()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqlServerSql("Server=localhost;Database=shared");
        services.AddHeadlessFencing(setup => setup.UseSqlServer("Server=localhost;Database=own"));

        await using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<SqlServerFencingOptions>>().Value;

        // then
        options.ConnectionString.Should().Be("Server=localhost;Database=own");
    }

    [Fact]
    public async Task should_throw_naming_add_sqlserver_sql_when_the_parameterless_overload_has_no_shared_connection()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessFencing(setup => setup.UseSqlServer());

        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<SqlServerFencingOptions>>().Value;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddSqlServerSql(connectionString)*");
    }
}
