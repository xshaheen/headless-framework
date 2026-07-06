// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Features.Definitions;
using Headless.Features.Entities;
using Headless.Features.Models;
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

    protected override void AddFeaturesDbContextFactory(IServiceCollection services)
    {
        services.AddDbContextFactory<CustomSchemaFeaturesDbContext>(options =>
            options.UseNpgsql(Fixture.SqlConnectionString)
        );
    }

    protected override void UseFeaturesEntityFramework(HeadlessFeaturesSetupBuilder setup)
    {
        setup.UseEntityFramework<CustomSchemaFeaturesDbContext>();
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
        // given — a context that does NOT override ConfigureFeaturesStorage, so the storage
        // options keep their defaults. Asserting the EF model mapping exercises the no-configuration
        // path directly, without mutating the shared fixture schema.
        await using var db = _CreateDefaultSchemaContext();

        // when
        var valuesEntity = db.Model.FindEntityType(typeof(FeatureValueRecord));
        var definitionsEntity = db.Model.FindEntityType(typeof(FeatureDefinitionRecord));
        var groupDefinitionsEntity = db.Model.FindEntityType(typeof(FeatureGroupDefinitionRecord));

        // then
        valuesEntity.Should().NotBeNull();
        valuesEntity!.GetSchema().Should().Be("features");
        valuesEntity.GetTableName().Should().Be("FeatureValues");
        definitionsEntity.Should().NotBeNull();
        definitionsEntity!.GetSchema().Should().Be("features");
        definitionsEntity.GetTableName().Should().Be("FeatureDefinitions");
        groupDefinitionsEntity.Should().NotBeNull();
        groupDefinitionsEntity!.GetSchema().Should().Be("features");
        groupDefinitionsEntity.GetTableName().Should().Be("FeatureGroupDefinitions");
    }

    [Fact]
    public async Task should_round_trip_feature_value_under_custom_schema()
    {
        // given
        using var host = await _CreateHostWithCustomTablesAsync(b =>
            b.Services.AddFeatureDefinitionProvider<FeaturesDefinitionProvider>()
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
        services.AddDbContextFactory<SharedFeaturesDbContext>(options =>
            options.UseNpgsql(Fixture.SqlConnectionString)
        );
        services.AddHeadlessFeatures(setup =>
        {
            setup.ConfigureStorage(ConfigureFeaturesStorage);
            setup.UseEntityFramework<SharedFeaturesDbContext>();
        });
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
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CustomSchemaFeaturesDbContext>>();
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

    private DefaultSchemaFeaturesContext _CreateDefaultSchemaContext()
    {
        // No ConfigureStorage call → FeaturesStorageOptions stays at its defaults
        // (schema "features" + default table names).
        var options = new DbContextOptionsBuilder<DefaultSchemaFeaturesContext>()
            .UseNpgsql(Fixture.SqlConnectionString)
            .Options;

        return new DefaultSchemaFeaturesContext(options, Options.Create(new FeaturesStorageOptions()));
    }

    private sealed class DefaultSchemaFeaturesContext(
        DbContextOptions<DefaultSchemaFeaturesContext> options,
        IOptions<FeaturesStorageOptions> storageOptions
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessFeatures(storageOptions.Value);
        }
    }

    private sealed class SharedFeaturesDbContext(
        DbContextOptions<SharedFeaturesDbContext> options,
        IOptions<FeaturesStorageOptions> storageOptions
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessFeatures(storageOptions.Value);
        }
    }

    private sealed class CustomSchemaFeaturesDbContext(
        DbContextOptions<CustomSchemaFeaturesDbContext> options,
        IOptions<FeaturesStorageOptions> storageOptions
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessFeatures(storageOptions.Value);
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
