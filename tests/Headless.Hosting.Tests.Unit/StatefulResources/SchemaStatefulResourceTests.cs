// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Hosting.Initialization.Schema;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.Initialization;

namespace Tests.StatefulResources;

public sealed class SchemaStatefulResourceTests : TestBase, IDisposable
{
    private readonly SqliteSchemaDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task should_offer_one_schema_resource_per_contribution_and_fail_check_until_set_up()
    {
        // given
        await using var provider = _Provider(_db.Contribution("Widgets", "s", _CreateWidgets));
        var resource = await _SingleAsync(provider);

        // when
        var beforeSetup = () => resource.CheckAsync(AbortToken);

        // then
        resource.Type.Should().Be("Schema");
        resource.Name.Should().Be("Sqlite/s/Widgets");
        await beforeSetup
            .Should()
            .ThrowAsync<StatefulResourceException>()
            .WithMessage("Steps 1 of Widgets are not applied. Run 'resources setup'*");
    }

    [Fact]
    public async Task should_create_the_tables_on_setup_so_check_passes_and_statistics_count_the_steps()
    {
        // given
        await using var provider = _Provider(_db.Contribution("Widgets", "s", _CreateWidgets));

        // when
        await provider.SetupResourcesAsync(AbortToken);

        // then
        _db.Execute("INSERT INTO s_widgets (id) VALUES (1)");
        var resource = await _SingleAsync(provider);
        await resource.CheckAsync(AbortToken);
        var status = await resource.GetStatusAsync(AbortToken);
        status
            .Should()
            .Equal(
                new("steps", "1"),
                new("applied", "1"),
                new("missing", "0"),
                new("changed", "0"),
                new("unknown", "0")
            );
    }

    [Fact]
    public async Task should_apply_an_export_only_contribution_the_host_never_verifies()
    {
        // given an additional outbox's contribution, which the host's own runner leaves to the outbox
        var template = _db.Contribution("Widgets", "s", _CreateWidgets);
        var exportOnly = new SchemaContribution(
            "Outbox",
            template.Dialect,
            template.CreateConnection,
            "s",
            [new("1", "outbox", "CREATE TABLE IF NOT EXISTS s_outbox (id INTEGER)")],
            exportOnly: true
        );
        await using var provider = _Provider(exportOnly);

        // when
        await provider.SetupResourcesAsync(AbortToken);

        // then
        _db.Execute("INSERT INTO s_outbox (id) VALUES (1)");
    }

    [Fact]
    public async Task should_fail_check_naming_the_step_when_its_sql_changed_after_it_was_applied()
    {
        // given a database that recorded version 1 with other SQL
        await using (var original = _Provider(_db.Contribution("Widgets", "s", _CreateWidgets)))
        {
            await original.SetupResourcesAsync(AbortToken);
        }

        var edited = new SchemaStep("1", "widgets", "CREATE TABLE IF NOT EXISTS s_widgets (id INTEGER, name TEXT)");
        await using var provider = _Provider(_db.Contribution("Widgets", "s", edited));

        // when
        var act = async () => await (await _SingleAsync(provider)).CheckAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<StatefulResourceException>().WithMessage("*were applied with different SQL*");
    }

    [Fact]
    public async Task should_report_clear_and_teardown_as_unsupported_when_no_object_is_declared()
    {
        await using var provider = _Provider(_db.Contribution("Widgets", "s", _CreateWidgets));
        var resource = await _SingleAsync(provider);

        await FluentActions
            .Awaiting(() => resource.ClearStateAsync(AbortToken))
            .Should()
            .ThrowAsync<StatefulResourceUnsupportedException>()
            .WithMessage("*declares no schema objects*");
        await FluentActions
            .Awaiting(() => resource.TeardownAsync(AbortToken))
            .Should()
            .ThrowAsync<StatefulResourceUnsupportedException>();
    }

    [Fact]
    public async Task should_clear_rows_but_keep_host_state_tables_and_history()
    {
        // given
        await using var provider = _Provider(_Declared());
        await provider.SetupResourcesAsync(AbortToken);
        _db.Execute("INSERT INTO s_widgets (id) VALUES (1)");
        _db.Execute("INSERT INTO s_definitions (id) VALUES (1)");

        // when
        await provider.ResetResourceStateAsync(AbortToken);

        // then
        _Count("s_widgets").Should().Be(0);
        _Count("s_definitions").Should().Be(1, "a host-state table keeps its rows");
        _Count("s_history").Should().Be(1);
    }

    [Fact]
    public async Task should_drop_the_objects_and_forget_the_history_so_setup_recreates_them()
    {
        // given
        await using var provider = _Provider(_Declared());
        await provider.SetupResourcesAsync(AbortToken);
        _db.Execute("INSERT INTO s_widgets (id) VALUES (1)");

        // when
        await provider.TeardownResourcesAsync(AbortToken);

        // then
        _Count("s_history").Should().Be(0);
        FluentActions.Invoking(() => _db.Execute("SELECT * FROM s_widgets")).Should().Throw<Exception>();
        await FluentActions
            .Awaiting(async () => await (await _SingleAsync(provider)).CheckAsync(AbortToken))
            .Should()
            .ThrowAsync<StatefulResourceException>();

        // and when
        await provider.SetupResourcesAsync(AbortToken);

        // then
        _Count("s_widgets").Should().Be(0);
        await (await _SingleAsync(provider)).CheckAsync(AbortToken);
    }

    [Fact]
    public async Task should_tear_down_a_resource_that_was_never_set_up()
    {
        await using var provider = _Provider(_Declared());

        var act = () => provider.TeardownResourcesAsync(AbortToken);

        await act.Should().NotThrowAsync("there is no history table yet, so nothing is recorded to forget");
    }

    private SchemaContribution _Declared()
    {
        var template = _db.Contribution("Widgets", "s", _CreateWidgets);

        return new SchemaContribution(
            "Widgets",
            template.Dialect,
            template.CreateConnection,
            "s",
            [
                new(
                    "1",
                    "widgets",
                    "CREATE TABLE IF NOT EXISTS s_definitions (id INTEGER PRIMARY KEY); "
                        + "CREATE TABLE IF NOT EXISTS s_widgets (id INTEGER, definition INTEGER REFERENCES s_definitions (id))"
                ),
            ],
            hostStateTables: ["s_definitions"],
            objects: [SchemaObject.Table("s_definitions"), SchemaObject.Table("s_widgets")]
        );
    }

    private long _Count(string table)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_db.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table}";

        return (long)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task should_offer_a_feature_registered_twice_for_one_database_once()
    {
        await using var provider = _Provider(
            _db.Contribution("Widgets", "s", _CreateWidgets),
            _db.Contribution("Widgets", "s", _CreateWidgets)
        );

        (await _SingleAsync(provider)).Name.Should().Be("Sqlite/s/Widgets");
    }

    [Fact]
    public async Task should_fail_a_host_start_naming_resources_setup_when_a_step_is_missing()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        var contribution = _db.Contribution("Widgets", "s", _CreateWidgets);
        builder.Services.AddHeadlessSchemaContribution(_ => contribution);
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<SchemaRunnerException>().WithMessage("*'resources setup'*");
    }

    [Fact]
    public async Task should_verify_only_after_setup_when_the_host_starts_its_services_concurrently()
    {
        // given a host that runs every StartingAsync at once, the verify included
        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<HostOptions>(options => options.ServicesStartConcurrently = true);
        var contribution = _db.Contribution("Widgets", "s", _CreateWidgets);
        builder.Services.AddHeadlessSchemaContribution(_ => contribution);
        builder.Services.AddHeadlessResourceSetupOnStartup();
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);

        // then
        _db.Execute("INSERT INTO s_widgets (id) VALUES (1)");
        await host.StopAsync(AbortToken);
    }

    private static SchemaStep _CreateWidgets =>
        new("1", "widgets", "CREATE TABLE IF NOT EXISTS s_widgets (id INTEGER)");

    private static ServiceProvider _Provider(params SchemaContribution[] contributions)
    {
        var services = new ServiceCollection();

        foreach (var contribution in contributions)
        {
            services.AddHeadlessSchemaContribution(_ => contribution);
        }

        return services.BuildServiceProvider();
    }

    private static async Task<IStatefulResource> _SingleAsync(IServiceProvider provider) =>
        (
            await provider
                .GetServices<IStatefulResourceSource>()
                .Should()
                .ContainSingle()
                .Which.GetResourcesAsync(AbortToken)
        )
            .Should()
            .ContainSingle()
            .Which;
}
