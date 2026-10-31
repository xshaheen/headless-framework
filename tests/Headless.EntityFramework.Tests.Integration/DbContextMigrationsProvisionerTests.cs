// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// <c>AddDbContextMigrations</c> offers a context's migrations as a resource: <c>resources provision</c> applies them,
/// and a host start fails, naming each pending migration, until they are applied.
/// </summary>
public sealed class DbContextMigrationsProvisionerTests : TestBase, IDisposable
{
    // An in-memory database lives as long as one connection to it stays open.
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public DbContextMigrationsProvisionerTests()
    {
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void should_register_one_resource_named_after_the_context_when_registration_is_repeated()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddDbContextMigrations<MigratedDbContext>();
        services.AddDbContextMigrations<MigratedDbContext>();

        // then
        using var provider = services.BuildServiceProvider();
        var resource = provider.GetServices<IResourceProvisioner>().Should().ContainSingle().Which;
        resource.Kind.Should().Be("EntityFramework");
        resource.Name.Should().Be(nameof(MigratedDbContext));
        resource.StartupCheck.Should().Be(ResourceStartupCheck.FailStart);
        resource.CanPurge.Should().BeFalse();
        resource.CanDelete.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_the_start_naming_each_pending_migration_on_a_fresh_database()
    {
        // given
        using var host = _BuildHost();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        (
            await act.Should()
                .ThrowAsync<ResourceProvisioningException>()
                .WithMessage("*EntityFramework MigratedDbContext*")
        )
            .WithInnerException<ResourceProvisioningException>()
            .WithMessage(
                $"Migrations {CreateWidgets.Id}, {AddWidgetName.Id} of {nameof(MigratedDbContext)} are not applied. Run 'resources provision'*"
            );
        _TableExists("widgets").Should().BeFalse("the host start only verifies");
    }

    [Fact]
    public async Task should_start_once_resources_provision_applied_every_migration()
    {
        // given
        using var host = _BuildHost();

        // when
        await host.Services.ProvisionResourcesAsync(AbortToken);
        await host.StartAsync(AbortToken);

        // then
        _TableExists("widgets").Should().BeTrue();
        var status = await host.Services.GetServices<IResourceProvisioner>().Single().GetStatusAsync(AbortToken);
        status
            .Should()
            .Equal(new KeyValuePair<string, string>("applied", "2"), new KeyValuePair<string, string>("pending", "0"));
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_apply_migrations_after_every_schema_resource_when_the_schema_resource_is_registered_later()
    {
        // given: registration order alone would run the migrations first
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContext<MigratedDbContext>(options =>
            options
                .UseSqlite(_connection)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
        );
        builder.Services.AddDbContextMigrations<MigratedDbContext>();
        var schema = new RecordingSchemaProvisioner(() => _TableExists("widgets"));
        builder.Services.AddResourceProvisioner(_ => schema);
        using var host = builder.Build();

        // when
        await host.Services.ProvisionResourcesAsync(AbortToken);

        // then
        schema.WidgetsExistedWhenProvisioned.Should().BeFalse("schema DDL lands before any app migration");
        _TableExists("widgets").Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_naming_the_context_and_its_factory_when_neither_is_registered()
    {
        // given
        var services = new ServiceCollection();
        services.AddDbContextMigrations<MigratedDbContext>();
        await using var provider = services.BuildServiceProvider();
        var resource = provider.GetServices<IResourceProvisioner>().Single();

        // when
        var act = () => resource.VerifyAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{nameof(MigratedDbContext)}*{nameof(IDbContextFactory<>)}*");
    }

    private IHost _BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContext<MigratedDbContext>(options =>
            options
                .UseSqlite(_connection)
                // The hand-written migrations below carry no model snapshot.
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
        );
        builder.Services.AddDbContextMigrations<MigratedDbContext>();

        return builder.Build();
    }

    private bool _TableExists(string table)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);

        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }
}

// Stands in for a schema-runner contribution: records what the database held when it ran.
internal sealed class RecordingSchemaProvisioner(Func<bool> widgetsExist) : IResourceProvisioner
{
    public bool? WidgetsExistedWhenProvisioned { get; private set; }

    public string Kind => SchemaRunner.ResourceKind;

    public string Name => "Sqlite/main/Recording";

    public Task ProvisionAsync(CancellationToken cancellationToken)
    {
        WidgetsExistedWhenProvisioned = widgetsExist();

        return Task.CompletedTask;
    }

    public Task VerifyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PurgeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DeleteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<KeyValuePair<string, string>>>([]);
}

internal sealed class MigratedDbContext(DbContextOptions<MigratedDbContext> options) : DbContext(options);

[DbContext(typeof(MigratedDbContext))]
[Migration(Id)]
internal sealed class CreateWidgets : Migration
{
    public const string Id = "20260101000000_CreateWidgets";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "widgets",
            columns: table => new { id = table.Column<int>(nullable: false) },
            constraints: table => table.PrimaryKey("pk_widgets", x => x.id)
        );
    }
}

[DbContext(typeof(MigratedDbContext))]
[Migration(Id)]
internal sealed class AddWidgetName : Migration
{
    public const string Id = "20260102000000_AddWidgetName";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "name", table: "widgets", nullable: true);
    }
}
