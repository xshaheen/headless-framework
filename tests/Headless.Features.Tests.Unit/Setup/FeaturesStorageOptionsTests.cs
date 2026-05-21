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
    public void should_validate_storage_option_fields(
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
}
