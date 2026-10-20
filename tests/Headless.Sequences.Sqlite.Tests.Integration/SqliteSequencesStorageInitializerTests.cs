// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Sequences;
using Headless.Sequences.Sqlite;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// The SQLite table lifecycle: created once at startup, safe to re-run and to race, never created lazily, and
/// failing loudly when the database cannot be opened. Also the provider options' own validation.
/// </summary>
[Collection<SqliteSequencesFixture>]
public sealed class SqliteSequencesStorageInitializerTests(SqliteSequencesFixture fixture) : TestBase
{
    [Fact]
    public async Task should_fail_the_start_with_the_driver_error_when_database_unreachable()
    {
        // A file in a directory that does not exist cannot be created, so the open fails.
        var unreachable = $"Data Source={Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "x.db")}";
        using var host = _CreateHost(options => options.ConnectionString = unreachable, setupResources: false);

        // The startup check fails the start; the schema runner names the features whose connection failed and keeps
        // the driver error as the cause.
        (
            await FluentActions
                .Awaiting(() => host.StartAsync(AbortToken))
                .Should()
                .ThrowAsync<ResourceProvisioningException>()
        )
            .WithInnerException<SchemaRunnerException>()
            .WithInnerException<SqliteException>();
    }

    [Fact]
    public async Task should_leave_one_table_when_hosts_initialize_concurrently()
    {
        const string schema = "sequences_sqlite_concurrent";
        var hosts = Enumerable.Range(0, 5).Select(_ => _CreateHost(schema: schema)).ToArray();

        try
        {
            await Task.WhenAll(hosts.Select(h => h.StartAsync(AbortToken)));

            (await _CountTablesAsync(schema)).Should().Be(1);
            (await _CountHistoryRowsAsync(schema)).Should().Be(1);
        }
        finally
        {
            foreach (var host in hosts)
            {
                host.Dispose();
            }
        }
    }

    [Fact]
    public async Task should_leave_one_table_when_setup_runs_twice()
    {
        const string schema = "sequences_sqlite_rerun";
        using var host = _CreateHost(schema: schema);
        await host.Services.ProvisionResourcesAsync(AbortToken);
        await host.Services.ProvisionResourcesAsync(AbortToken);

        (await _CountTablesAsync(schema)).Should().Be(1);
    }

    [Fact]
    public async Task should_number_in_a_configured_schema_and_table()
    {
        const string schema = "sequences_sqlite_custom";
        const string table = "counters";
        using var host = _CreateHost(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.Schema = schema;
            options.TableName = table;
        });
        await host.StartAsync(AbortToken);
        var generator = host.Services.GetRequiredService<ISequenceGenerator>();

        (await generator.NextAsync("custom", cancellationToken: AbortToken)).Should().Be(1);
        (await generator.NextAsync("custom", cancellationToken: AbortToken)).Should().Be(2);

        (await fixture.ReadValueAsync(new SequenceKey("", "custom", ""), schema, table, AbortToken)).Should().Be(2);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_fail_the_start_naming_resources_setup_when_the_table_is_missing()
    {
        const string schema = "sequences_sqlite_no_init";
        using var host = _CreateHost(
            options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.Schema = schema;
            },
            setupResources: false
        );

        await FluentActions
            .Awaiting(() => host.StartAsync(AbortToken))
            .Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*resources provision*");

        (await _CountTablesAsync(schema)).Should().Be(0);
    }

    [Fact]
    public void should_reject_an_empty_connection_string()
    {
        using var host = _CreateHost(options => options.ConnectionString = "");

        var act = () => host.Services.GetRequiredService<IOptions<SqliteSequencesOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*ConnectionString*");
    }

    [Fact]
    public void should_reject_an_invalid_schema_identifier()
    {
        using var host = _CreateHost(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.Schema = "bad schema\"; DROP TABLE x; --";
        });

        var act = () => host.Services.GetRequiredService<IOptions<SqliteSequencesOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Schema*");
    }

    [Fact]
    public void should_reject_a_blank_connection_string_at_the_call_site()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessSequences(setup => setup.UseSqlite("  "));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_default_the_schema_to_the_shared_headless_schema()
    {
        new SqliteSequencesOptions().Schema.Should().Be("headless");
    }

    private static IHost _CreateHost(Action<SqliteSequencesOptions> configure, bool setupResources = true)
    {
        var builder = Host.CreateApplicationBuilder();

        if (setupResources)
        {
            builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);
        }

        builder.Services.AddHeadlessSequences(setup => setup.UseSqlite(configure));

        return builder.Build();
    }

    private IHost _CreateHost(string schema)
    {
        return _CreateHost(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.Schema = schema;
        });
    }

    private Task<long> _CountTablesAsync(string schema)
    {
        return fixture.ScalarAsync(
            $"SELECT count(*) FROM sqlite_schema WHERE type = 'table' AND name = '{schema}_{SqliteSequencesOptions.DefaultTableName}'",
            AbortToken
        );
    }

    private Task<long> _CountHistoryRowsAsync(string schema)
    {
        return fixture.ScalarAsync($"SELECT count(*) FROM \"{schema}_{SchemaRunner.HistoryTableName}\"", AbortToken);
    }
}
