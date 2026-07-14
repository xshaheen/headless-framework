// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions;
using Headless.Permissions.Definitions;
using Headless.Permissions.Entities;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Tests.TestSetup;

namespace Tests;

public sealed class PermissionsCustomSchemaTests(PermissionsTestFixture fixture) : PermissionsTestBase(fixture)
{
    private const string _Schema = "myapp_permissions";
    private const string _GrantsTableName = "tbl_permission_grants";
    private const string _DefinitionsTableName = "tbl_permission_definitions";
    private const string _GroupDefinitionsTableName = "tbl_permission_group_definitions";
    private const string _PermissionName = "CustomSchemaPermission";

    protected override void ConfigurePermissionsStorage(PermissionsStorageOptions options)
    {
        options.Schema = _Schema;
        options.PermissionGrantsTableName = _GrantsTableName;
        options.PermissionDefinitionsTableName = _DefinitionsTableName;
        options.PermissionGroupDefinitionsTableName = _GroupDefinitionsTableName;
    }

    [Fact]
    public async Task should_create_tables_in_custom_schema_with_custom_names()
    {
        // given
        using var host = await _CreateHostWithCustomTablesAsync();

        // when
        var grantsTableExists = await _TableExistsAsync(_Schema, _GrantsTableName);
        var definitionsTableExists = await _TableExistsAsync(_Schema, _DefinitionsTableName);
        var groupDefinitionsTableExists = await _TableExistsAsync(_Schema, _GroupDefinitionsTableName);
        var defaultGrantsTableExists = await _TableExistsAsync("permissions", _GrantsTableName);

        // then
        grantsTableExists.Should().BeTrue();
        definitionsTableExists.Should().BeTrue();
        groupDefinitionsTableExists.Should().BeTrue();
        defaultGrantsTableExists.Should().BeFalse();
    }

    [Fact]
    public async Task should_keep_default_schema_and_table_names_without_storage_configuration()
    {
        // given — a context that does NOT override ConfigurePermissionsStorage, so the storage
        // options keep their defaults. Asserting the EF model mapping exercises the no-configuration
        // path directly, without mutating the shared fixture schema.
        await using var db = _CreateDefaultSchemaContext();

        // when
        var grantsEntity = db.Model.FindEntityType(typeof(PermissionGrantRecord));
        var definitionsEntity = db.Model.FindEntityType(typeof(PermissionDefinitionRecord));
        var groupDefinitionsEntity = db.Model.FindEntityType(typeof(PermissionGroupDefinitionRecord));

        // then
        grantsEntity.Should().NotBeNull();
        grantsEntity!.GetSchema().Should().Be("permissions");
        grantsEntity.GetTableName().Should().Be("PermissionGrants");
        definitionsEntity.Should().NotBeNull();
        definitionsEntity!.GetSchema().Should().Be("permissions");
        definitionsEntity.GetTableName().Should().Be("PermissionDefinitions");
        groupDefinitionsEntity.Should().NotBeNull();
        groupDefinitionsEntity!.GetSchema().Should().Be("permissions");
        groupDefinitionsEntity.GetTableName().Should().Be("PermissionGroupDefinitions");
    }

    [Fact]
    public async Task should_round_trip_permission_grant_under_custom_schema()
    {
        // given
        using var host = await _CreateHostWithCustomTablesAsync(b =>
            b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>()
        );
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();
        var userId = Guid.NewGuid().ToString();

        // when
        await permissionManager.GrantToUserAsync(_PermissionName, userId, AbortToken);

        // then
        (await _TableHasRowsAsync(_Schema, _GrantsTableName))
            .Should()
            .BeTrue();
        (await _TableHasRowsAsync("permissions", "PermissionGrants")).Should().BeFalse();
    }

    [Fact]
    public async Task should_apply_custom_storage_options_in_shared_dbcontext_without_constructor_injection()
    {
        // given
        var services = new ServiceCollection();
        services.AddDbContextFactory<SharedPermissionsContext>(options =>
            options.UseNpgsql(Fixture.SqlConnectionString)
        );
        services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureStorage(ConfigurePermissionsStorage);
            setup.UseEntityFramework<SharedPermissionsContext>();
        });
        await using var provider = services.BuildServiceProvider();
        await using var db = await provider
            .GetRequiredService<IDbContextFactory<SharedPermissionsContext>>()
            .CreateDbContextAsync(AbortToken);

        // when
        var grantsEntity = db.Model.FindEntityType(typeof(PermissionGrantRecord));
        var definitionsEntity = db.Model.FindEntityType(typeof(PermissionDefinitionRecord));
        var groupDefinitionsEntity = db.Model.FindEntityType(typeof(PermissionGroupDefinitionRecord));

        // then
        grantsEntity.Should().NotBeNull();
        grantsEntity!.GetSchema().Should().Be(_Schema);
        grantsEntity.GetTableName().Should().Be(_GrantsTableName);
        definitionsEntity.Should().NotBeNull();
        definitionsEntity!.GetSchema().Should().Be(_Schema);
        definitionsEntity.GetTableName().Should().Be(_DefinitionsTableName);
        groupDefinitionsEntity.Should().NotBeNull();
        groupDefinitionsEntity!.GetSchema().Should().Be(_Schema);
        groupDefinitionsEntity.GetTableName().Should().Be(_GroupDefinitionsTableName);
    }

    private async Task<IHost> _CreateHostWithCustomTablesAsync(Action<IHostApplicationBuilder>? configure = null)
    {
        await Fixture.ResetAsync();
        using var setupHost = CreateHost(configure);
        await _RecreateCustomTablesAsync(setupHost.Services);

        return CreateHost(configure);
    }

    private async Task _RecreateCustomTablesAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PermissionsTestDbContext>>();
        await using var db = await factory.CreateDbContextAsync(AbortToken);

        await db.Database.ExecuteSqlRawAsync($"""DROP SCHEMA IF EXISTS "{_Schema}" CASCADE""", AbortToken);
        await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA \"{_Schema}\"", AbortToken);

        var creator = db.GetService<IRelationalDatabaseCreator>();
        await creator.CreateTablesAsync(AbortToken);
    }

    private async Task<bool> _TableExistsAsync(string schema, string tableName)
    {
        await using var connection = new NpgsqlConnection(Fixture.SqlConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @table
            )
            """,
            connection
        );
        command.Parameters.AddWithValue(nameof(schema), schema);
        command.Parameters.AddWithValue("table", tableName);

        return (bool)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private async Task<bool> _TableHasRowsAsync(string schema, string tableName)
    {
        await using var connection = new NpgsqlConnection(Fixture.SqlConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            $"""SELECT EXISTS (SELECT 1 FROM "{schema}"."{tableName}")""",
            connection
        );

        return (bool)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private DefaultSchemaPermissionsContext _CreateDefaultSchemaContext()
    {
        // No ConfigureStorage call → PermissionsStorageOptions stays at its defaults
        // (schema "permissions" + default table names).
        var options = new DbContextOptionsBuilder<DefaultSchemaPermissionsContext>()
            .UseNpgsql(Fixture.SqlConnectionString)
            .Options;

        return new DefaultSchemaPermissionsContext(options, Options.Create(new PermissionsStorageOptions()));
    }

    private sealed class DefaultSchemaPermissionsContext(
        DbContextOptions<DefaultSchemaPermissionsContext> options,
        IOptions<PermissionsStorageOptions> storageOptions
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessPermissions(storageOptions.Value);
        }
    }

    private sealed class SharedPermissionsContext(
        DbContextOptions<SharedPermissionsContext> options,
        IOptions<PermissionsStorageOptions> storageOptions
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessPermissions(storageOptions.Value);
        }
    }

    [UsedImplicitly]
    private sealed class PermissionsDefinitionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("CustomSchemaGroup").AddChild(_PermissionName);
        }
    }
}
