// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Features.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class FeaturesStorageOptionsTests
{
    [Theory]
    [InlineData("", "FeatureValues", "FeatureDefinitions", "FeatureGroupDefinitions")]
    [InlineData("features", "", "FeatureDefinitions", "FeatureGroupDefinitions")]
    [InlineData("features", "FeatureValues", "", "FeatureGroupDefinitions")]
    [InlineData("features", "FeatureValues", "FeatureDefinitions", "")]
    [InlineData("   ", "FeatureValues", "FeatureDefinitions", "FeatureGroupDefinitions")]
    [InlineData("features", "   ", "FeatureDefinitions", "FeatureGroupDefinitions")]
    [InlineData("features", "FeatureValues", "   ", "FeatureGroupDefinitions")]
    [InlineData("features", "FeatureValues", "FeatureDefinitions", "   ")]
    public void should_reject_storage_options_when_any_field_is_blank(
        string schema,
        string valuesTable,
        string definitionsTable,
        string groupDefinitionsTable
    )
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessFeatures(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = schema;
                options.FeatureValuesTableName = valuesTable;
                options.FeatureDefinitionsTableName = definitionsTable;
                options.FeatureGroupDefinitionsTableName = groupDefinitionsTable;
            });
            setup.UseEntityFramework<TestDbContext>();
        });
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<FeaturesStorageOptions>>();

        // when
        var act = () => options.Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_accept_storage_options_when_all_fields_are_non_blank()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessFeatures(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = "custom_features";
                options.FeatureValuesTableName = "tbl_feature_values";
                options.FeatureDefinitionsTableName = "tbl_feature_definitions";
                options.FeatureGroupDefinitionsTableName = "tbl_feature_group_definitions";
            });
            setup.UseEntityFramework<TestDbContext>();
        });
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<FeaturesStorageOptions>>();

        // when
        var act = () => options.Value;

        // then
        var resolved = act.Should().NotThrow().Subject;
        resolved.Schema.Should().Be("custom_features");
        resolved.FeatureValuesTableName.Should().Be("tbl_feature_values");
        resolved.FeatureDefinitionsTableName.Should().Be("tbl_feature_definitions");
        resolved.FeatureGroupDefinitionsTableName.Should().Be("tbl_feature_group_definitions");
    }

    [Fact]
    public void should_accept_storage_options_when_left_at_defaults()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessFeatures(setup => setup.UseEntityFramework<TestDbContext>());
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<FeaturesStorageOptions>>();

        // when
        var act = () => options.Value;

        // then
        var resolved = act.Should().NotThrow().Subject;
        resolved.Schema.Should().Be("features");
        resolved.FeatureValuesTableName.Should().Be("FeatureValues");
        resolved.FeatureDefinitionsTableName.Should().Be("FeatureDefinitions");
        resolved.FeatureGroupDefinitionsTableName.Should().Be("FeatureGroupDefinitions");
    }

    [Fact]
    public void should_apply_feature_model_configuration_when_entities_are_already_discovered()
    {
        // given
        var storageOptions = new FeaturesStorageOptions
        {
            Schema = "custom_features",
            FeatureValuesTableName = "custom_feature_values",
            FeatureDefinitionsTableName = "custom_feature_definitions",
            FeatureGroupDefinitionsTableName = "custom_feature_groups",
        };
        using var context = new ExistingFeaturesEntityDbContext(
            new DbContextOptionsBuilder<ExistingFeaturesEntityDbContext>().UseSqlite("DataSource=:memory:").Options,
            storageOptions
        );

        // when
        var featureValueEntity = context.Model.FindEntityType(typeof(FeatureValueRecord));
        var featureDefinitionEntity = context.Model.FindEntityType(typeof(FeatureDefinitionRecord));
        var groupDefinitionEntity = context.Model.FindEntityType(typeof(FeatureGroupDefinitionRecord));

        // then
        featureValueEntity.Should().NotBeNull();
        featureValueEntity!.GetSchema().Should().Be("custom_features");
        featureValueEntity.GetTableName().Should().Be("custom_feature_values");
        featureDefinitionEntity.Should().NotBeNull();
        featureDefinitionEntity!.GetTableName().Should().Be("custom_feature_definitions");
        groupDefinitionEntity.Should().NotBeNull();
        groupDefinitionEntity!.GetTableName().Should().Be("custom_feature_groups");
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);

    private sealed class ExistingFeaturesEntityDbContext(
        DbContextOptions<ExistingFeaturesEntityDbContext> options,
        FeaturesStorageOptions storageOptions
    ) : DbContext(options)
    {
        public DbSet<FeatureValueRecord> FeatureValues => Set<FeatureValueRecord>();

        public DbSet<FeatureDefinitionRecord> FeatureDefinitions => Set<FeatureDefinitionRecord>();

        public DbSet<FeatureGroupDefinitionRecord> FeatureGroupDefinitions => Set<FeatureGroupDefinitionRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessFeatures(storageOptions);
        }
    }
}
