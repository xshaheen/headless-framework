// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Sequences;
using Headless.Sequences.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests;

/// <summary>
/// The PostgreSQL table lifecycle: created once at startup, safe to re-run and to race, never created lazily, and
/// failing loudly when the database cannot be reached. Also the provider options' own validation.
/// </summary>
[Collection<PostgreSqlSequencesFixture>]
public sealed class PostgreSqlSequencesStorageInitializerTests(PostgreSqlSequencesFixture fixture) : TestBase
{
    [Fact]
    public async Task should_fail_the_start_with_the_driver_error_when_database_unreachable()
    {
        // Port 1 accepts no connections, so the TCP connect fails before any authentication is attempted.
        const string unreachable =
            "Host=127.0.0.1;Port=1;Database=missing;Username=postgres;Password=placeholder-never-used;Timeout=2";
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
            .WithInnerException<NpgsqlException>();
    }

    [Fact]
    public async Task should_leave_one_table_and_one_primary_key_when_hosts_initialize_concurrently()
    {
        const string schema = "sequences_pg_concurrent";
        await _DropSchemaAsync(schema);
        var hosts = Enumerable.Range(0, 5).Select(_ => _CreateHost(schema: schema)).ToArray();

        try
        {
            await Task.WhenAll(hosts.Select(h => h.StartAsync(AbortToken)));

            (await _CountTablesAsync(schema)).Should().Be(1);
            (await _CountPrimaryKeysAsync(schema)).Should().Be(1);
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
    public async Task should_leave_one_table_and_one_primary_key_when_setup_runs_twice()
    {
        const string schema = "sequences_pg_rerun";
        await _DropSchemaAsync(schema);
        using var host = _CreateHost(schema: schema);
        await host.Services.ProvisionResourcesAsync(AbortToken);
        await host.Services.ProvisionResourcesAsync(AbortToken);

        (await _CountTablesAsync(schema)).Should().Be(1);
        (await _CountPrimaryKeysAsync(schema)).Should().Be(1);
    }

    [Fact]
    public async Task should_number_in_a_configured_schema_and_table()
    {
        const string schema = "sequences_pg_custom";
        const string table = "counters";
        await _DropSchemaAsync(schema);
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
        const string schema = "sequences_pg_no_init";
        await _DropSchemaAsync(schema);
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

        var act = () => host.Services.GetRequiredService<IOptions<PostgreSqlSequencesOptions>>().Value;

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

        var act = () => host.Services.GetRequiredService<IOptions<PostgreSqlSequencesOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Schema*");
    }

    [Fact]
    public void should_reject_a_blank_connection_string_at_the_call_site()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessSequences(setup => setup.UsePostgreSql("  "));

        act.Should().Throw<ArgumentException>();
    }

    private IHost _CreateHost(Action<PostgreSqlSequencesOptions> configure, bool setupResources = true)
    {
        var builder = Host.CreateApplicationBuilder();

        if (setupResources)
        {
            builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);
        }

        builder.Services.AddHeadlessSequences(setup => setup.UsePostgreSql(configure));

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

    private Task _DropSchemaAsync(string schema)
    {
        return fixture.ExecuteAsync($"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""", AbortToken);
    }

    private Task<int> _CountTablesAsync(string schema)
    {
        return fixture.ScalarAsync(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table",
            AbortToken,
            ("schema", schema),
            ("table", PostgreSqlSequencesOptions.DefaultTableName)
        );
    }

    private Task<int> _CountPrimaryKeysAsync(string schema)
    {
        return fixture.ScalarAsync(
            """
            SELECT count(*)
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = @schema AND t.relname = @table AND c.contype = 'p'
            """,
            AbortToken,
            ("schema", schema),
            ("table", PostgreSqlSequencesOptions.DefaultTableName)
        );
    }
}
