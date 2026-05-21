// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Features.Definitions;
using Headless.Features.Entities;
using Headless.Features.Models;
using Headless.Features.Storage.EntityFramework;
using Headless.Features.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Tests.TestSetup;

namespace Tests;

public sealed class FeaturesCustomSchemaTests(FeaturesTestFixture fixture) : FeaturesTestBase(fixture)
{
    private const string _Schema = "myapp_features";
    private const string _ValuesTableName = "tbl_feature_values";
    private const string _DefinitionsTableName = "tbl_feature_definitions";
    private const string _GroupDefinitionsTableName = "tbl_feature_group_definitions";
    private const string _FeatureName = "CustomSchemaFeature";

    protected override void ConfigureFeaturesStorage(FeaturesStorageOptions options)
    {
        options.Schema = _Schema;
        options.FeatureValuesTableName = _ValuesTableName;
        options.FeatureDefinitionsTableName = _DefinitionsTableName;
        options.FeatureGroupDefinitionsTableName = _GroupDefinitionsTableName;
    }

    [Fact]
    public async Task should_create_tables_in_custom_schema_with_custom_names()
    {
        // given
        using var host = await _CreateHostWithCustomTablesAsync();

        // when
        var valuesTableExists = await _TableExistsAsync(_Schema, _ValuesTableName);
        var definitionsTableExists = await _TableExistsAsync(_Schema, _DefinitionsTableName);
        var groupDefinitionsTableExists = await _TableExistsAsync(_Schema, _GroupDefinitionsTableName);
        var defaultValuesTableExists = await _TableExistsAsync("features", _ValuesTableName);

        // then
        valuesTableExists.Should().BeTrue();
        definitionsTableExists.Should().BeTrue();
        groupDefinitionsTableExists.Should().BeTrue();
        defaultValuesTableExists.Should().BeFalse();
    }

    [Fact]
    public async Task should_keep_default_schema_and_table_names_without_storage_configuration()
    {
        // given
        await Fixture.ResetAsync();

        // when
        var valuesTableExists = await _TableExistsAsync("features", "FeatureValues");
        var definitionsTableExists = await _TableExistsAsync("features", "FeatureDefinitions");
        var groupDefinitionsTableExists = await _TableExistsAsync("features", "FeatureGroupDefinitions");

        // then
        valuesTableExists.Should().BeTrue();
        definitionsTableExists.Should().BeTrue();
        groupDefinitionsTableExists.Should().BeTrue();
    }

    [Fact]
    public async Task should_round_trip_feature_value_under_custom_schema()
    {
        // given
        using var host = await _CreateHostWithCustomTablesAsync(
            b => b.Services.AddFeatureDefinitionProvider<FeaturesDefinitionProvider>()
        );
        await using var scope = host.Services.CreateAsyncScope();
        var featureManager = scope.ServiceProvider.GetRequiredService<IFeatureManager>();
        var editionId = Guid.NewGuid().ToString();
        const string value = "true";

        // when
        await featureManager.SetForEditionAsync(_FeatureName, value, editionId);
        var storedValue = await featureManager.GetForEditionAsync(_FeatureName, editionId);

        // then
        storedValue.Value.Should().Be(value);
        (await _TableHasRowsAsync(_Schema, _ValuesTableName)).Should().BeTrue();
        (await _TableHasRowsAsync("features", "FeatureValues")).Should().BeFalse();
    }

    [Fact]
    public async Task should_apply_custom_storage_options_in_shared_dbcontext_without_constructor_injection()
    {
        // given
        var services = new ServiceCollection();
        services.AddDbContextFactory<SharedFeaturesDbContext>(options => options.UseNpgsql(Fixture.SqlConnectionString));
        services.AddFeaturesManagementDbContextStorage<SharedFeaturesDbContext>(ConfigureFeaturesStorage);
        await using var provider = services.BuildServiceProvider();
        await using var db = await provider
            .GetRequiredService<IDbContextFactory<SharedFeaturesDbContext>>()
            .CreateDbContextAsync(AbortToken);

        // when
        var valuesEntity = db.Model.FindEntityType(typeof(FeatureValueRecord));
        var definitionsEntity = db.Model.FindEntityType(typeof(FeatureDefinitionRecord));
        var groupDefinitionsEntity = db.Model.FindEntityType(typeof(FeatureGroupDefinitionRecord));

        // then
        valuesEntity.Should().NotBeNull();
        valuesEntity!.GetSchema().Should().Be(_Schema);
        valuesEntity.GetTableName().Should().Be(_ValuesTableName);
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
        var factory = new FeaturesStorageModelCacheKeyFactory();
        var contextA = _BuildContextWithOptions(new FeaturesStorageOptions
        {
            Schema = "features_a",
            FeatureValuesTableName = "FeatureValues",
            FeatureDefinitionsTableName = "FeatureDefinitions",
            FeatureGroupDefinitionsTableName = "FeatureGroupDefinitions",
        });
        var contextB = _BuildContextWithOptions(new FeaturesStorageOptions
        {
            Schema = "features_b",
            FeatureValuesTableName = "FeatureValues",
            FeatureDefinitionsTableName = "FeatureDefinitions",
            FeatureGroupDefinitionsTableName = "FeatureGroupDefinitions",
        });

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
        var factory = new FeaturesStorageModelCacheKeyFactory();
        var optionsValues = new FeaturesStorageOptions
        {
            Schema = "shared",
            FeatureValuesTableName = "Values",
            FeatureDefinitionsTableName = "Definitions",
            FeatureGroupDefinitionsTableName = "Groups",
        };
        var contextA = _BuildContextWithOptions(optionsValues);
        var contextB = _BuildContextWithOptions(new FeaturesStorageOptions
        {
            Schema = optionsValues.Schema,
            FeatureValuesTableName = optionsValues.FeatureValuesTableName,
            FeatureDefinitionsTableName = optionsValues.FeatureDefinitionsTableName,
            FeatureGroupDefinitionsTableName = optionsValues.FeatureGroupDefinitionsTableName,
        });

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
        var factory = new FeaturesStorageModelCacheKeyFactory();
        var context = _BuildContextWithOptions(new FeaturesStorageOptions());

        // when
        var runtimeKey = factory.Create(context, designTime: false);
        var designTimeKey = factory.Create(context, designTime: true);

        // then
        runtimeKey.Should().NotBe(designTimeKey);
    }

    private static FeaturesDbContext _BuildContextWithOptions(FeaturesStorageOptions storageOptions)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<FeaturesStorageOptions>>(Options.Create(storageOptions));
        var sp = services.BuildServiceProvider();

        var dbOptions = new DbContextOptionsBuilder<FeaturesDbContext>()
            .UseNpgsql("Host=localhost;Database=test;Username=test;Password=test")
            .UseApplicationServiceProvider(sp)
            .Options;

        return new FeaturesDbContext(dbOptions)
        {
            FeatureValues = null!,
            FeatureDefinitions = null!,
            FeatureGroupDefinitions = null!,
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
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FeaturesDbContext>>();
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

    private sealed class SharedFeaturesDbContext(DbContextOptions<SharedFeaturesDbContext> options)
        : DbContext(options), IFeaturesDbContext
    {
        public DbSet<FeatureValueRecord> FeatureValues => Set<FeatureValueRecord>();

        public DbSet<FeatureDefinitionRecord> FeatureDefinitions => Set<FeatureDefinitionRecord>();

        public DbSet<FeatureGroupDefinitionRecord> FeatureGroupDefinitions => Set<FeatureGroupDefinitionRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddFeaturesConfiguration(this);
        }
    }

    [UsedImplicitly]
    private sealed class FeaturesDefinitionProvider : IFeatureDefinitionProvider
    {
        public void Define(IFeatureDefinitionContext context)
        {
            context.AddGroup("CustomSchemaGroup").AddChild(_FeatureName, "false");
        }
    }
}
