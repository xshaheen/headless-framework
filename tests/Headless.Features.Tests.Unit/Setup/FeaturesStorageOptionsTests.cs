// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Storage.EntityFramework;
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
        services.AddFeaturesManagementDbContextStorage<FeaturesDbContext>(options =>
        {
            options.Schema = schema;
            options.FeatureValuesTableName = valuesTable;
            options.FeatureDefinitionsTableName = definitionsTable;
            options.FeatureGroupDefinitionsTableName = groupDefinitionsTable;
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
        services.AddFeaturesManagementDbContextStorage<FeaturesDbContext>(options =>
        {
            options.Schema = "custom_features";
            options.FeatureValuesTableName = "tbl_feature_values";
            options.FeatureDefinitionsTableName = "tbl_feature_definitions";
            options.FeatureGroupDefinitionsTableName = "tbl_feature_group_definitions";
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
        services.AddFeaturesManagementDbContextStorage<FeaturesDbContext>();
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
}
