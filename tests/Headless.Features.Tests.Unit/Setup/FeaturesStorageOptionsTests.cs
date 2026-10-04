// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Hosting.Initialization;
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
        resolved.Schema.Should().Be("headless");
        resolved.FeatureValuesTableName.Should().BeNull();
        resolved.FeatureDefinitionsTableName.Should().BeNull();
        resolved.FeatureGroupDefinitionsTableName.Should().BeNull();
    }

    [Theory]
    [InlineData(StorageNamingStyle.PascalCase, "FeatureValues", "FeatureDefinitions", "FeatureGroupDefinitions")]
    [InlineData(StorageNamingStyle.SnakeCase, "feature_values", "feature_definitions", "feature_group_definitions")]
    public void should_resolve_default_table_names_in_the_database_naming_style(
        StorageNamingStyle style,
        string valuesTable,
        string definitionsTable,
        string groupDefinitionsTable
    )
    {
        // given
        var options = new FeaturesStorageOptions();

        // when / then
        options.ResolveFeatureValuesTableName(style).Should().Be(valuesTable);
        options.ResolveFeatureDefinitionsTableName(style).Should().Be(definitionsTable);
        options.ResolveFeatureGroupDefinitionsTableName(style).Should().Be(groupDefinitionsTable);
    }

    [Fact]
    public void should_resolve_configured_table_names_verbatim_in_every_naming_style()
    {
        // given
        var options = new FeaturesStorageOptions
        {
            FeatureValuesTableName = "MyValues",
            FeatureDefinitionsTableName = "MyDefinitions",
            FeatureGroupDefinitionsTableName = "MyGroups",
        };

        // when / then
        options.ResolveFeatureValuesTableName(StorageNamingStyle.SnakeCase).Should().Be("MyValues");
        options.ResolveFeatureDefinitionsTableName(StorageNamingStyle.SnakeCase).Should().Be("MyDefinitions");
        options.ResolveFeatureGroupDefinitionsTableName(StorageNamingStyle.PascalCase).Should().Be("MyGroups");
    }

    [Fact]
    public void should_accept_the_snake_case_defaults_when_configured_explicitly()
    {
        // given — the conventional names themselves must fit, or the defaults' derived names would be truncated
        var services = _ServicesWithTableNames("feature_values", "feature_definitions", "feature_group_definitions");
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<FeaturesStorageOptions>>().Value;

        // then
        act.Should().NotThrow();
    }

    // The longest derived PostgreSQL names are ix_{values}_name_provider_name_null_provider_key (40 bytes besides the
    // table), ix_{definitions}_group_name (14), and ix_{groups}_name (8), so the longest names that fit 63 bytes are
    // 23, 49, and 55 characters.
    [Theory]
    [InlineData(23, 1, 1, true)]
    [InlineData(24, 1, 1, false)]
    [InlineData(1, 49, 1, true)]
    [InlineData(1, 50, 1, false)]
    [InlineData(1, 1, 55, true)]
    [InlineData(1, 1, 56, false)]
    public void should_refuse_a_table_name_whose_derived_postgresql_names_exceed_63_bytes(
        int valuesLength,
        int definitionsLength,
        int groupsLength,
        bool accepted
    )
    {
        // given
        var services = _ServicesWithTableNames(
            new string('v', valuesLength),
            new string('d', definitionsLength),
            new string('g', groupsLength)
        );
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<FeaturesStorageOptions>>().Value;

        // then
        if (accepted)
        {
            act.Should().NotThrow();
        }
        else
        {
            act.Should().Throw<OptionsValidationException>().WithMessage("*PostgreSQL truncates identifiers*");
        }
    }

    private static ServiceCollection _ServicesWithTableNames(string values, string definitions, string groups)
    {
        var services = new ServiceCollection();
        services.AddHeadlessFeatures(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.FeatureValuesTableName = values;
                options.FeatureDefinitionsTableName = definitions;
                options.FeatureGroupDefinitionsTableName = groups;
            });
            setup.UseEntityFramework<TestDbContext>();
        });

        return services;
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
            modelBuilder.AddHeadlessFeatures(storageOptions, StorageNamingStyle.PascalCase);
        }
    }
}
