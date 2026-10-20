// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Hosting.Initialization.Schema;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.Initialization;

namespace Tests.Provisioning;

public sealed class SchemaProvisionerTests : TestBase, IDisposable
{
    private readonly SqliteSchemaDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task should_offer_one_schema_resource_per_contribution_and_fail_verify_until_provisioned()
    {
        // given
        await using var provider = _Provider(_db.Contribution("Widgets", "s", _CreateWidgets));
        var resource = await _SingleAsync(provider);

        // when
        var beforeSetup = () => resource.VerifyAsync(AbortToken);

        // then
        resource.Kind.Should().Be("Schema");
        resource.Name.Should().Be("Sqlite/s/Widgets");
        await beforeSetup
            .Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("Steps 1 of Widgets are not applied. Run 'resources provision'*");
    }

    [Fact]
    public async Task should_create_the_tables_on_provision_so_verify_passes_and_status_count_the_steps()
    {
        // given
        await using var provider = _Provider(_db.Contribution("Widgets", "s", _CreateWidgets));

        // when
        await provider.ProvisionResourcesAsync(AbortToken);

        // then
        _db.Execute("INSERT INTO s_widgets (id) VALUES (1)");
        var resource = await _SingleAsync(provider);
        await resource.VerifyAsync(AbortToken);
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
        await provider.ProvisionResourcesAsync(AbortToken);

        // then
        _db.Execute("INSERT INTO s_outbox (id) VALUES (1)");
    }

    [Fact]
    public async Task should_fail_verify_naming_the_step_when_its_sql_changed_after_it_was_applied()
    {
        // given a database that recorded version 1 with other SQL
        await using (var original = _Provider(_db.Contribution("Widgets", "s", _CreateWidgets)))
        {
            await original.ProvisionResourcesAsync(AbortToken);
        }

        var edited = new SchemaStep("1", "widgets", "CREATE TABLE IF NOT EXISTS s_widgets (id INTEGER, name TEXT)");
        await using var provider = _Provider(_db.Contribution("Widgets", "s", edited));

        // when
        var act = async () => await (await _SingleAsync(provider)).VerifyAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<ResourceProvisioningException>().WithMessage("*were applied with different SQL*");
    }

    [Fact]
    public async Task should_report_purge_and_delete_as_unsupported_when_no_object_is_declared()
    {
        await using var provider = _Provider(_db.Contribution("Widgets", "s", _CreateWidgets));
        var resource = await _SingleAsync(provider);

        resource.CanPurge.Should().BeFalse();
        resource.CanDelete.Should().BeFalse();
    }

    [Fact]
    public async Task should_purge_rows_but_keep_host_state_tables_and_history()
    {
        // given
        await using var provider = _Provider(_Declared());
        await provider.ProvisionResourcesAsync(AbortToken);
        _db.Execute("INSERT INTO s_widgets (id) VALUES (1)");
        _db.Execute("INSERT INTO s_definitions (id) VALUES (1)");

        // when
        await provider.PurgeResourcesAsync(AbortToken);

        // then
        _Count("s_widgets").Should().Be(0);
        _Count("s_definitions").Should().Be(1, "a host-state table keeps its rows");
        _Count("s_history").Should().Be(1);
    }

    [Fact]
    public async Task should_purge_only_under_the_schema_lock()
    {
        // given — another runner holds the database's schema lock
        var runner = new SchemaRunner([_Declared()], timeProvider: _db.Clock, lockTimeout: TimeSpan.Zero);
        await runner.ApplyAsync(AbortToken);
        _db.Execute("INSERT INTO s_widgets (id) VALUES (1)");
        _db.LockUnavailable = true;

        // when
        var act = () => runner.PurgeAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<SchemaRunnerException>().WithMessage("*timed out*schema lock*");
        _Count("s_widgets").Should().Be(1, "a clear that cannot take the lock deletes nothing");
    }

    [Fact]
    public async Task should_drop_the_objects_and_forget_the_history_so_provision_recreates_them()
    {
        // given
        await using var provider = _Provider(_Declared());
        await provider.ProvisionResourcesAsync(AbortToken);
        _db.Execute("INSERT INTO s_widgets (id) VALUES (1)");

        // when
        await provider.DeleteResourcesAsync(AbortToken);

        // then
        _Count("s_history").Should().Be(0);
        FluentActions.Invoking(() => _db.Execute("SELECT * FROM s_widgets")).Should().Throw<Exception>();
        await FluentActions
            .Awaiting(async () => await (await _SingleAsync(provider)).VerifyAsync(AbortToken))
            .Should()
            .ThrowAsync<ResourceProvisioningException>();

        // and when
        await provider.ProvisionResourcesAsync(AbortToken);

        // then
        _Count("s_widgets").Should().Be(0);
        await (await _SingleAsync(provider)).VerifyAsync(AbortToken);
    }

    [Fact]
    public async Task should_delete_a_resource_that_was_never_provisioned()
    {
        await using var provider = _Provider(_Declared());

        var act = () => provider.DeleteResourcesAsync(AbortToken);

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
    public async Task should_name_the_database_only_when_one_feature_and_schema_live_in_two_databases()
    {
        // given
        using var other = new SqliteSchemaDatabase();
        await using var provider = _Provider(
            _db.Contribution("Widgets", "s", _CreateWidgets),
            other.Contribution("Widgets", "s", _CreateWidgets),
            _db.Contribution("Gadgets", "s", _CreateWidgets)
        );

        // when
        var names = (
            await provider.GetServices<IResourceProvisionerSource>().Single().GetProvisionersAsync(AbortToken)
        ).Select(r => r.Name);

        // then
        names.Should().HaveCount(3).And.OnlyHaveUniqueItems().And.Contain("Sqlite/s/Gadgets");
        names.Where(n => n.StartsWith("Sqlite/s/Widgets@", StringComparison.Ordinal)).Should().HaveCount(2);
    }

    [Fact]
    public async Task should_fail_a_host_start_naming_resources_provision_when_a_step_is_missing()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        var contribution = _db.Contribution("Widgets", "s", _CreateWidgets);
        builder.Services.AddHeadlessSchemaContribution(_ => contribution);
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<SchemaRunnerException>().WithMessage("*'resources provision'*");
    }

    [Fact]
    public async Task should_verify_only_after_provisioning_when_the_host_starts_its_services_concurrently()
    {
        // given a host that runs every StartingAsync at once, the verify included
        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<HostOptions>(options => options.ServicesStartConcurrently = true);
        var contribution = _db.Contribution("Widgets", "s", _CreateWidgets);
        builder.Services.AddHeadlessSchemaContribution(_ => contribution);
        builder.Services.AddDeployStepsOnStartup();
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

    private static async Task<IResourceProvisioner> _SingleAsync(IServiceProvider provider) =>
        (
            await provider
                .GetServices<IResourceProvisionerSource>()
                .Should()
                .ContainSingle()
                .Which.GetProvisionersAsync(AbortToken)
        )
            .Should()
            .ContainSingle()
            .Which;
}
