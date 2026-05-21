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
        // given
        await Fixture.ResetAsync();

        // when
        var grantsTableExists = await _TableExistsAsync("permissions", "PermissionGrants");
        var definitionsTableExists = await _TableExistsAsync("permissions", "PermissionDefinitions");
        var groupDefinitionsTableExists = await _TableExistsAsync("permissions", "PermissionGroupDefinitions");

        // then
        grantsTableExists.Should().BeTrue();
        definitionsTableExists.Should().BeTrue();
        groupDefinitionsTableExists.Should().BeTrue();
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
        services.AddDbContextFactory<SharedPermissionsDbContext>(options =>
            options.UseNpgsql(Fixture.SqlConnectionString)
        );
        services.AddPermissionsManagementDbContextStorage<SharedPermissionsDbContext>(ConfigurePermissionsStorage);
        await using var provider = services.BuildServiceProvider();
        await using var db = await provider
            .GetRequiredService<IDbContextFactory<SharedPermissionsDbContext>>()
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

    [Fact]
    public void should_produce_distinct_model_cache_keys_for_distinct_storage_options()
    {
        // given
        var factory = new PermissionsStorageModelCacheKeyFactory();
        var contextA = _BuildContextWithOptions(
            new PermissionsStorageOptions
            {
                Schema = "permissions_a",
                PermissionGrantsTableName = "PermissionGrants",
                PermissionDefinitionsTableName = "PermissionDefinitions",
                PermissionGroupDefinitionsTableName = "PermissionGroupDefinitions",
            }
        );
        var contextB = _BuildContextWithOptions(
            new PermissionsStorageOptions
            {
                Schema = "permissions_b",
                PermissionGrantsTableName = "PermissionGrants",
                PermissionDefinitionsTableName = "PermissionDefinitions",
                PermissionGroupDefinitionsTableName = "PermissionGroupDefinitions",
            }
        );

        // when
        var keyA = factory.Create(contextA, designTime: false);
        var keyB = factory.Create(contextB, designTime: false);

        // then
        keyA.Should().NotBe(keyB);
        keyA.Should().Be(factory.Create(contextA, designTime: false));
    }

    [Fact]
    public void should_produce_equal_model_cache_keys_for_equal_storage_options()
    {
        // given
        var factory = new PermissionsStorageModelCacheKeyFactory();
        var optionsValues = new PermissionsStorageOptions
        {
            Schema = "shared",
            PermissionGrantsTableName = "Grants",
            PermissionDefinitionsTableName = "Definitions",
            PermissionGroupDefinitionsTableName = "Groups",
        };
        var contextA = _BuildContextWithOptions(optionsValues);
        var contextB = _BuildContextWithOptions(
            new PermissionsStorageOptions
            {
                Schema = optionsValues.Schema,
                PermissionGrantsTableName = optionsValues.PermissionGrantsTableName,
                PermissionDefinitionsTableName = optionsValues.PermissionDefinitionsTableName,
                PermissionGroupDefinitionsTableName = optionsValues.PermissionGroupDefinitionsTableName,
            }
        );

        // when
        var keyA = factory.Create(contextA, designTime: false);
        var keyB = factory.Create(contextB, designTime: false);

        // then
        keyA.Should().Be(keyB);
    }

    [Fact]
    public void should_distinguish_designtime_in_model_cache_key()
    {
        // given
        var factory = new PermissionsStorageModelCacheKeyFactory();
        var context = _BuildContextWithOptions(new PermissionsStorageOptions());

        // when
        var runtimeKey = factory.Create(context, designTime: false);
        var designTimeKey = factory.Create(context, designTime: true);

        // then
        runtimeKey.Should().NotBe(designTimeKey);
    }

    private static PermissionsDbContext _BuildContextWithOptions(PermissionsStorageOptions storageOptions)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(storageOptions));
        var sp = services.BuildServiceProvider();

        var dbOptions = new DbContextOptionsBuilder<PermissionsDbContext>()
            .UseNpgsql("Host=localhost;Database=test;Username=test;Password=test")
            .UseApplicationServiceProvider(sp)
            .Options;

        return new PermissionsDbContext(dbOptions)
        {
            PermissionGrants = null!,
            PermissionDefinitions = null!,
            PermissionGroupDefinitions = null!,
        };
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
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PermissionsDbContext>>();
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
        command.Parameters.AddWithValue("schema", schema);
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

    private sealed class SharedPermissionsDbContext(DbContextOptions<SharedPermissionsDbContext> options)
        : DbContext(options),
            IPermissionsDbContext
    {
        public DbSet<PermissionGrantRecord> PermissionGrants => Set<PermissionGrantRecord>();

        public DbSet<PermissionDefinitionRecord> PermissionDefinitions => Set<PermissionDefinitionRecord>();

        public DbSet<PermissionGroupDefinitionRecord> PermissionGroupDefinitions =>
            Set<PermissionGroupDefinitionRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddPermissionsConfiguration(this);
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
