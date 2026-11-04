// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests;

/// <summary>
/// Resolves the provider options without a database, so the class stays outside the container collection.
/// </summary>
public sealed class PostgreSqlDistributedLocksSharedConnectionTests : TestBase
{
    [Fact]
    public async Task should_use_the_shared_connection_string_when_using_the_parameterless_overload()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlSql("Host=localhost;Database=shared");
        services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql());

        await using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<PostgreSqlDistributedLockOptions>>().Value;

        // then
        options.ConnectionString.Should().Be("Host=localhost;Database=shared");
    }

    [Fact]
    public async Task should_open_connections_on_the_shared_string_unchanged_when_using_the_parameterless_overload()
    {
        // given
        const string sharedConnectionString = "Host=localhost;Database=shared";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlSql(sharedConnectionString);
        services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql());

        await using var provider = services.BuildServiceProvider();

        // when
        var source = provider.GetRequiredService<PostgresLockDataSource>();
        await using var connection = source.CreateConnection();

        // then (no KeepAlive rewrite, so the connection lands in the pool the other features on this string use)
        connection.ConnectionString.Should().Be(sharedConnectionString);
        provider.GetService<NpgsqlDataSource>().Should().BeNull();
    }

    [Fact]
    public async Task should_reuse_the_registered_data_source_when_using_the_parameterless_overload()
    {
        // given
        await using var appDataSource = NpgsqlDataSource.Create("Host=localhost;Database=app");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(appDataSource);
        services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql());

        await using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<PostgreSqlDistributedLockOptions>>().Value;
        var source = provider.GetRequiredService<PostgresLockDataSource>();

        // then
        options.DataSource.Should().BeSameAs(appDataSource);
        source.ConnectionString.Should().Be(appDataSource.ConnectionString);
        provider.GetRequiredService<NpgsqlDataSource>().Should().BeSameAs(appDataSource);
    }

    [Fact]
    public async Task should_use_the_shared_connection_string_when_the_registered_data_source_multiplexes()
    {
        // given
        await using var appDataSource = NpgsqlDataSource.Create("Host=localhost;Database=app;Multiplexing=true");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(appDataSource);
        services.AddPostgreSqlSql("Host=localhost;Database=shared");
        services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql());

        await using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<PostgreSqlDistributedLockOptions>>().Value;

        // then
        options.DataSource.Should().BeNull();
        options.ConnectionString.Should().Be("Host=localhost;Database=shared");
    }

    [Fact]
    public async Task should_fail_validation_naming_multiplexing_when_the_only_source_multiplexes()
    {
        // given
        await using var appDataSource = NpgsqlDataSource.Create("Host=localhost;Database=app;Multiplexing=true");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(appDataSource);
        services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql());

        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<PostgreSqlDistributedLockOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>().WithMessage("*Multiplexing*");
    }

    [Fact]
    public void should_fail_validation_naming_multiplexing_when_the_connection_string_multiplexes()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql("Host=localhost;Database=own;Multiplexing=true")
        );

        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<PostgreSqlDistributedLockOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>().WithMessage("*Multiplexing*");
    }

    [Fact]
    public async Task should_not_register_its_own_data_source_as_the_container_data_source()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql("Host=localhost;Database=own"));

        await using var provider = services.BuildServiceProvider();

        // when
        var source = provider.GetRequiredService<PostgresLockDataSource>();

        // then (the explicit string still builds a provider-owned pool with the KeepAlive default)
        new NpgsqlConnectionStringBuilder(source.ConnectionString)
            .KeepAlive.Should()
            .Be(30);
        provider.GetService<NpgsqlDataSource>().Should().BeNull();
    }

    [Fact]
    public async Task should_prefer_its_own_connection_string_over_the_shared_connection()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlSql("Host=localhost;Database=shared");
        services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql("Host=localhost;Database=own"));

        await using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<PostgreSqlDistributedLockOptions>>().Value;

        // then
        options.ConnectionString.Should().Be("Host=localhost;Database=own");
    }

    [Fact]
    public async Task should_throw_naming_add_postgresql_sql_when_the_parameterless_overload_has_no_shared_connection()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql());

        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<PostgreSqlDistributedLockOptions>>().Value;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddPostgreSqlSql(connectionString)*");
    }
}
