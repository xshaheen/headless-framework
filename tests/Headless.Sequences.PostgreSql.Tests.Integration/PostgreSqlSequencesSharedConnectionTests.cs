// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Sequences.PostgreSql;
using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Resolves the provider options without a database, so the class stays outside the container collection.
/// </summary>
public sealed class PostgreSqlSequencesSharedConnectionTests : TestBase
{
    [Fact]
    public async Task should_use_the_shared_connection_string_when_using_the_parameterless_overload()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlSql("Host=localhost;Database=shared");
        services.AddHeadlessSequences(setup => setup.UsePostgreSql());

        await using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<PostgreSqlSequencesOptions>>().Value;

        // then
        options.ConnectionString.Should().Be("Host=localhost;Database=shared");
    }

    [Fact]
    public async Task should_prefer_its_own_connection_string_over_the_shared_connection()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlSql("Host=localhost;Database=shared");
        services.AddHeadlessSequences(setup => setup.UsePostgreSql("Host=localhost;Database=own"));

        await using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<PostgreSqlSequencesOptions>>().Value;

        // then
        options.ConnectionString.Should().Be("Host=localhost;Database=own");
    }

    [Fact]
    public async Task should_throw_naming_add_postgresql_sql_when_the_parameterless_overload_has_no_shared_connection()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessSequences(setup => setup.UsePostgreSql());

        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<PostgreSqlSequencesOptions>>().Value;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddPostgreSqlSql(connectionString)*");
    }
}
