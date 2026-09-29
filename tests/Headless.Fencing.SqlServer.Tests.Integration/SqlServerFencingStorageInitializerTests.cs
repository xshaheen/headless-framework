// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Fencing.SqlServer;
using Headless.Hosting.Initialization;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// The SQL Server lease storage lifecycle: created once at startup, safe to re-run and to race, never created
/// lazily, and failing loudly when the database cannot be reached. Also the provider options' own validation.
/// </summary>
[Collection<SqlServerFencingFixture>]
public sealed class SqlServerFencingStorageInitializerTests(SqlServerFencingFixture fixture) : TestBase
{
    [Fact]
    public async Task should_throw_and_keep_initializer_unmarked_when_database_unreachable()
    {
        // Port 1 accepts no connections, so the TCP connect fails before any login is attempted.
        const string unreachable =
            "Server=127.0.0.1,1;Database=missing;User ID=sa;Password=placeholder-never-used;Connect Timeout=2;TrustServerCertificate=True";
        using var host = _CreateHost(options => options.ConnectionString = unreachable);

        await FluentActions
            .Awaiting(() => host.StartAsync(AbortToken))
            .Should()
            .ThrowAsync<Exception>()
            .Where(e => e is SqlException || e.InnerException is SqlException);

        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        initializer.IsInitialized.Should().BeFalse();

        await FluentActions
            .Awaiting(() => initializer.WaitForInitializationAsync(AbortToken))
            .Should()
            .ThrowAsync<SqlException>();
    }

    [Fact]
    public async Task should_create_each_object_once_when_hosts_initialize_concurrently()
    {
        const string schema = "fencing_mssql_concurrent";
        await _DropStorageAsync(schema);
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
        const string schema = "fencing_mssql_rerun";
        await _DropStorageAsync(schema);
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
        const string schema = "fencing_mssql_custom";
        await _DropStorageAsync(schema);
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
        const string schema = "fencing_mssql_no_init";
        await _DropStorageAsync(schema);
        using var host = _CreateHost(
            options =>
            {
                options.ConnectionString = fixture.LeaseConnectionString;
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

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(208, "invalid object name");
        (await _CountTablesAsync(schema)).Should().Be(0);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public void should_reject_an_empty_connection_string()
    {
        using var host = _CreateHost(options => options.ConnectionString = "");

        var act = () => host.Services.GetRequiredService<IOptions<SqlServerFencingOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*ConnectionString*");
    }

    [Fact]
    public void should_reject_an_invalid_schema_identifier()
    {
        using var host = _CreateHost(
            options => options.ConnectionString = fixture.LeaseConnectionString,
            "bad schema]; DROP TABLE x; --"
        );

        var act = () => host.Services.GetRequiredService<IOptions<FencingStorageOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Schema*");
    }

    [Fact]
    public void should_reject_a_blank_connection_string_at_the_call_site()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessFencing(setup => setup.UseSqlServer("  "));

        act.Should().Throw<ArgumentException>();
    }

    private static IHost _CreateHost(Action<SqlServerFencingOptions> configure, string? schema = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessFencing(setup =>
        {
            setup.UseSqlServer(configure);

            if (schema is not null)
            {
                setup.ConfigureStorage(storage => storage.Schema = schema);
            }
        });

        return builder.Build();
    }

    private IHost _CreateHost(string schema)
    {
        return _CreateHost(options => options.ConnectionString = fixture.LeaseConnectionString, schema);
    }

    private async Task _AssertObjectsAsync(string schema)
    {
        (await _CountTablesAsync(schema)).Should().Be(1);
        (
            await fixture.ScalarAsync(
                "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(@table) AND index_id > 0",
                AbortToken,
                ("table", $"{schema}.leases")
            )
        )
            .Should()
            .Be(3, "the clustered primary key, the active-expiry index, and the ended index");
        (
            await fixture.ScalarAsync(
                "SELECT COUNT(*) FROM sys.sequences WHERE schema_id = SCHEMA_ID(@schema)",
                AbortToken,
                ("schema", schema)
            )
        )
            .Should()
            .Be(1);
    }

    private Task _DropStorageAsync(string schema)
    {
        return fixture.ExecuteAsync(SqlServerFencingFixtureBase.DropStorageSql(schema), AbortToken);
    }

    private Task<int> _CountTablesAsync(string schema)
    {
        return fixture.ScalarAsync(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(@schema) AND name = 'leases'",
            AbortToken,
            ("schema", schema)
        );
    }
}
