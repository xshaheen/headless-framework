// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Coordination.Sqlite;
using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Resolves the provider options without a database, so the class stays outside the container collection.
/// </summary>
public sealed class SqliteCoordinationSharedConnectionTests : TestBase
{
    [Fact]
    public async Task should_use_the_shared_connection_string_when_using_the_parameterless_overload()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqliteSql("Data Source=shared.db");
        services.AddHeadlessCoordination(setup => setup.UseSqlite());

        await using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<SqliteCoordinationOptions>>().Value;

        // then
        options.ConnectionString.Should().Be("Data Source=shared.db");
    }

    [Fact]
    public async Task should_prefer_its_own_connection_string_over_the_shared_connection()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqliteSql("Data Source=shared.db");
        services.AddHeadlessCoordination(setup => setup.UseSqlite("Data Source=own.db"));

        await using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<SqliteCoordinationOptions>>().Value;

        // then
        options.ConnectionString.Should().Be("Data Source=own.db");
    }

    [Fact]
    public async Task should_throw_naming_add_sqlite_sql_when_the_parameterless_overload_has_no_shared_connection()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessCoordination(setup => setup.UseSqlite());

        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<SqliteCoordinationOptions>>().Value;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddSqliteSql(connectionString)*");
    }
}
