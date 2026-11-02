// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Schema;
using Headless.Search;
using Headless.Sql;
using Headless.Sql.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class PostgreSqlArabicSearchSchemaTests : TestBase
{
    private const string _ConnectionString = "Host=localhost;Database=search;Username=postgres;Password=unused";

    // The snapshot is the review surface for a rule change: every edit to the V1 table shows here as SQL.
    [Fact]
    public async Task should_export_the_v1_function_step()
    {
        await using var provider = _Provider(options => options.ConnectionString = _ConnectionString);

        var script = provider.GetRequiredService<SchemaRunner>().ExportScript(PostgreSqlSchemaDialect.Instance);

        await Verify(script).UseDirectory("Snapshots");
    }

    [Fact]
    public async Task should_create_the_function_in_the_configured_schema()
    {
        await using var provider = _Provider(options =>
        {
            options.ConnectionString = _ConnectionString;
            options.Schema = "search";
        });

        var script = provider.GetRequiredService<SchemaRunner>().ExportScript(PostgreSqlSchemaDialect.Instance);

        script.Should().Contain("CREATE FUNCTION \"search\".ar_norm_v1(value text)");
        script.Should().Contain("n.nspname = 'search' AND p.proname = 'ar_norm_v1'");
        script.Should().NotContain("CREATE OR REPLACE");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task should_fail_options_validation_when_connection_string_is_blank(string connectionString)
    {
        await using var provider = _Provider(options => options.ConnectionString = connectionString);

        var act = () =>
            provider.GetRequiredService<IOptions<Headless.Search.PostgreSql.PostgreSqlSearchOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*connection string*");
    }

    [Fact]
    public async Task should_fail_options_validation_when_schema_is_not_an_identifier()
    {
        await using var provider = _Provider(options =>
        {
            options.ConnectionString = _ConnectionString;
            options.Schema = "bad schema";
        });

        var act = () =>
            provider.GetRequiredService<IOptions<Headless.Search.PostgreSql.PostgreSqlSearchOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Schema*");
    }

    [Fact]
    public async Task should_bind_connection_string_and_schema_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("Search:ConnectionString", _ConnectionString),
                new KeyValuePair<string, string?>("Search:Schema", "search"),
            ])
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessSearch(setup => setup.UsePostgreSql(configuration.GetSection("Search")));
        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<Headless.Search.PostgreSql.PostgreSqlSearchOptions>>().Value;

        options.ConnectionString.Should().Be(_ConnectionString);
        options.Schema.Should().Be("search");
    }

    [Fact]
    public async Task should_use_the_shared_connection_when_use_postgresql_has_no_arguments()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlSql(_ConnectionString);
        services.AddHeadlessSearch(setup => setup.UsePostgreSql());
        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<Headless.Search.PostgreSql.PostgreSqlSearchOptions>>().Value;

        options.ConnectionString.Should().Be(_ConnectionString);
        options.Schema.Should().Be("headless");
    }

    private static ServiceProvider _Provider(Action<Headless.Search.PostgreSql.PostgreSqlSearchOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessSearch(setup => setup.UsePostgreSql(configure));

        return services.BuildServiceProvider();
    }
}
