// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Fencing.PostgreSql;
using Headless.Hosting.Initialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests;

/// <summary>
/// The PostgreSQL lease storage lifecycle: created once at startup, safe to re-run and to race, never created
/// lazily, and failing loudly when the database cannot be reached. Also the provider options' own validation.
/// </summary>
[Collection<PostgreSqlFencingFixture>]
public sealed class PostgreSqlFencingStorageInitializerTests(PostgreSqlFencingFixture fixture) : TestBase
{
    [Fact]
    public async Task should_throw_and_keep_initializer_unmarked_when_database_unreachable()
    {
        // Port 1 accepts no connections, so the TCP connect fails before any authentication is attempted.
        const string unreachable =
            "Host=127.0.0.1;Port=1;Database=missing;Username=postgres;Password=placeholder-never-used;Timeout=2";
        using var host = _CreateHost(options => options.ConnectionString = unreachable);

        await FluentActions
            .Awaiting(() => host.StartAsync(AbortToken))
            .Should()
            .ThrowAsync<Exception>()
            .Where(e => e is NpgsqlException || e.InnerException is NpgsqlException);

        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        initializer.IsInitialized.Should().BeFalse();

        await FluentActions
            .Awaiting(() => initializer.WaitForInitializationAsync(AbortToken))
            .Should()
            .ThrowAsync<NpgsqlException>();
    }

    [Fact]
    public async Task should_create_each_object_once_when_hosts_initialize_concurrently()
    {
        const string schema = "fencing_pg_concurrent";
        await _DropSchemaAsync(schema);
        var hosts = Enumerable.Range(0, 5).Select(_ => _CreateHost(schema)).ToArray();

        try
        {
            await Task.WhenAll(hosts.Select(h => h.StartAsync(AbortToken)));

            hosts
                .Select(h => h.Services.GetRequiredService<IEnumerable<IInitializer>>().Single().IsInitialized)
                .Should()
                .AllSatisfy(initialized => initialized.Should().BeTrue());
            await _AssertObjectsAsync(schema);
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
    public async Task should_create_each_object_once_when_the_initializer_runs_twice()
    {
        const string schema = "fencing_pg_rerun";
        await _DropSchemaAsync(schema);
        using var host = _CreateHost(schema);
        var initializer = host
            .Services.GetRequiredService<IEnumerable<IInitializer>>()
            .OfType<HostedInitializer>()
            .Single();

        await initializer.StartingAsync(AbortToken);
        await initializer.StartingAsync(AbortToken);

        initializer.IsInitialized.Should().BeTrue();
        await _AssertObjectsAsync(schema);
    }

    [Fact]
    public async Task should_grant_in_a_configured_schema()
    {
        const string schema = "fencing_pg_custom";
        await _DropSchemaAsync(schema);
        using var host = _CreateHost(schema);
        await host.StartAsync(AbortToken);
        var leases = host.Services.GetRequiredService<IFencedLeases>();

        var granted = await leases.GrantAsync("custom", "resource", TimeSpan.FromMinutes(1), AbortToken);

        granted.Status.Should().Be(LeaseGrantStatus.Granted);
        (await fixture.ReadLeaseAsync(new LeaseKey("", "custom", "resource"), schema, AbortToken))!
            .Generation.Should()
            .Be(granted.Lease!.Generation);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_not_create_the_table_when_initialization_is_off()
    {
        const string schema = "fencing_pg_no_init";
        await _DropSchemaAsync(schema);
        using var host = _CreateHost(
            options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.InitializeOnStartup = false;
            },
            schema
        );

        await host.StartAsync(AbortToken);

        host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single().IsInitialized.Should().BeTrue();
        (await _CountTablesAsync(schema)).Should().Be(0);

        // The consumer owns the table in this mode, so a call before it exists fails instead of creating it.
        var act = async () =>
            await host
                .Services.GetRequiredService<IFencedLeases>()
                .GrantAsync("x", "y", TimeSpan.FromMinutes(1), AbortToken);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42P01");
        (await _CountTablesAsync(schema)).Should().Be(0);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public void should_reject_an_empty_connection_string()
    {
        using var host = _CreateHost(options => options.ConnectionString = "");

        var act = () => host.Services.GetRequiredService<IOptions<PostgreSqlFencingOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*ConnectionString*");
    }

    [Fact]
    public void should_reject_an_invalid_schema_identifier()
    {
        using var host = _CreateHost(
            options => options.ConnectionString = fixture.ConnectionString,
            "bad schema\"; DROP TABLE x; --"
        );

        var act = () => host.Services.GetRequiredService<IOptions<FencingStorageOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Schema*");
    }

    [Fact]
    public void should_reject_a_blank_connection_string_at_the_call_site()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessFencing(setup => setup.UsePostgreSql("  "));

        act.Should().Throw<ArgumentException>();
    }

    private static IHost _CreateHost(Action<PostgreSqlFencingOptions> configure, string? schema = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessFencing(setup =>
        {
            setup.UsePostgreSql(configure);

            if (schema is not null)
            {
                setup.ConfigureStorage(storage => storage.Schema = schema);
            }
        });

        return builder.Build();
    }

    private IHost _CreateHost(string schema)
    {
        return _CreateHost(options => options.ConnectionString = fixture.ConnectionString, schema);
    }

    private async Task _AssertObjectsAsync(string schema)
    {
        (await _CountTablesAsync(schema)).Should().Be(1);
        (await _CountIndexesAsync(schema))
            .Should()
            .Be(3, "the primary key, the active-expiry index, and the ended index");
        (await _CountSequencesAsync(schema)).Should().Be(1);
    }

    private Task _DropSchemaAsync(string schema)
    {
        return fixture.ExecuteAsync($"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""", AbortToken);
    }

    private Task<int> _CountTablesAsync(string schema)
    {
        return fixture.ScalarAsync(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = @schema AND table_name = 'leases'",
            AbortToken,
            ("schema", schema)
        );
    }

    private Task<int> _CountIndexesAsync(string schema)
    {
        return fixture.ScalarAsync(
            "SELECT count(*) FROM pg_indexes WHERE schemaname = @schema AND tablename = 'leases'",
            AbortToken,
            ("schema", schema)
        );
    }

    private Task<int> _CountSequencesAsync(string schema)
    {
        return fixture.ScalarAsync(
            "SELECT count(*) FROM information_schema.sequences WHERE sequence_schema = @schema",
            AbortToken,
            ("schema", schema)
        );
    }
}
