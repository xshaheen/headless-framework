using Framework.Core;
using Framework.Exceptions;
using Framework.Features;
using Framework.Features.Definitions;
using Framework.Features.Models;
using Framework.Features.ValueProviders;
using Framework.Features.Values;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class FeatureManagerTests(FeaturesTestFixture fixture, ITestOutputHelper output)
    : FeaturesTestBase(fixture, output)
{
    private static readonly FeatureGroupDefinition[] _GroupDefinitions =
    [
        TestData.CreateGroupDefinition(4),
        TestData.CreateGroupDefinition(5),
        TestData.CreateGroupDefinition(7),
    ];

    // Not defined feature

    [Fact]
    public async Task get_should_error_when_not_defined_feature()
    {
        // given
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var featureManager = scope.ServiceProvider.GetRequiredService<IFeatureManager>();
        const string featureName = "NotDefined";
        const string editionId = "AnyEditionId";

        // when
        Func<Task<FeatureValue>> act = () => featureManager.GetForEditionAsync(featureName, editionId);

        // then
        await act.Should()
            .ThrowAsync<ConflictException>()
            .WithMessage("Conflict: features:undefined: The feature 'NotDefined' is undefined.");
    }

    [Fact]
    public async Task get_all_should_return_empty_when_not_defined_feature()
    {
        // given
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var featureManager = scope.ServiceProvider.GetRequiredService<IFeatureManager>();

        // when
        var features = await featureManager.GetAllForEditionAsync("AnyEditionId");

        // then
        features.Should().BeEmpty();
    }

    // Default value feature

    [Fact]
    public async Task should_get_default_value()
    {
        // given
        using var host = CreateHost(b => b.Services.AddFeatureDefinitionProvider<FeaturesDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var featureManager = scope.ServiceProvider.GetRequiredService<IFeatureManager>();
        var defaultFeatureValues = _GetDefaultFeatureValues();
        var oneOfFeatures = RandomHelper.GetRandomOfList(defaultFeatureValues);

        // when
        var features = await featureManager.GetAllDefaultAsync();
        var feature = await featureManager.GetDefaultAsync(oneOfFeatures.Name);

        // then
        features.Should().HaveCount(16);
        features.Should().BeEquivalentTo(defaultFeatureValues);
        feature.Should().NotBeNull();
        feature.Should().BeEquivalentTo(oneOfFeatures);
        feature.Value.Should().Be(oneOfFeatures.Value);
        feature.Provider.Should().NotBeNull();
        feature.Provider.Name.Should().Be(DefaultValueFeatureValueProvider.ProviderName);
        feature.Provider.Key.Should().BeNull();
    }

    // Fallback

    [Fact]
    public async Task should_fallback_to_default_value_when_edition_not_has_any_value()
    {
        // given
        using var host = CreateHost(b => b.Services.AddFeatureDefinitionProvider<FeaturesDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var featureManager = scope.ServiceProvider.GetRequiredService<IFeatureManager>();
        var defaultFeatureValues = _GetDefaultFeatureValues();
        var oneOfFeatures = RandomHelper.GetRandomOfList(defaultFeatureValues);
        const string editionId = "AnyEditionId";

        // when
        var features = await featureManager.GetAllForEditionAsync(editionId);
        var feature = await featureManager.GetForEditionAsync(oneOfFeatures.Name, editionId);

        // then
        features.Should().HaveCount(16);
        features.Should().BeEquivalentTo(defaultFeatureValues);
        feature.Should().NotBeNull();
        feature.Should().BeEquivalentTo(oneOfFeatures);
        feature.Value.Should().Be(oneOfFeatures.Value);
        feature.Provider.Should().NotBeNull();
        feature.Provider.Name.Should().Be(DefaultValueFeatureValueProvider.ProviderName);
        feature.Provider.Key.Should().BeNull();
    }

    [Fact]
    public async Task should_not_fallback_when_edition_not_has_any_value_and_no_fallback()
    {
        // given
        using var host = CreateHost(b => b.Services.AddFeatureDefinitionProvider<FeaturesDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var featureManager = scope.ServiceProvider.GetRequiredService<IFeatureManager>();
        var oneOfFeatures = _GroupDefinitions[0].Features[0];
        const string editionId = "AnyEditionId";

        // when
        var features = await featureManager.GetAllForEditionAsync(editionId, fallback: false);
        var feature = await featureManager.GetForEditionAsync(oneOfFeatures.Name, editionId, fallback: false);

        // then
        features.Should().BeEmpty();
        feature.Should().NotBeNull();
        feature.Name.Should().Be(oneOfFeatures.Name);
        feature.Value.Should().BeNull();
        feature.Provider.Should().BeNull();
    }

    // Grant & Revoke feature value

    [Fact]
    public async Task should_be_able_to_grant_and_revoke_features()
    {
        // given
        using var host = CreateHost(b => b.Services.AddFeatureDefinitionProvider<Feature1FeaturesDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var featureManager = scope.ServiceProvider.GetRequiredService<IFeatureManager>();
        const string featureName = "Feature1";
        const string editionId = "AnyEditionId";

        // pre conditions: not granted
        var feature = await featureManager.GetForEditionAsync(featureName, editionId);
        feature.Should().NotBeNull();
        feature.Value.Should().Be("false");
        feature.Name.Should().Be(featureName);
        feature.Provider.Should().NotBeNull();
        feature.Provider.Name.Should().Be(DefaultValueFeatureValueProvider.ProviderName);
        feature.Provider.Key.Should().BeNull();

        var features = await featureManager.GetAllForEditionAsync(feature.Provider.Key);
        features.Should().ContainSingle();
        features[0].Should().BeEquivalentTo(feature);

        // when: grant
        await featureManager.GrantToEditionAsync(featureName, editionId);

        // then: granted
        feature = await featureManager.GetForEditionAsync(featureName, editionId);
        feature.Should().NotBeNull();
        feature.Value.Should().Be("true");
        feature.Name.Should().Be(featureName);
        feature.Provider.Should().NotBeNull();
        feature.Provider.Name.Should().Be(EditionFeatureValueProvider.ProviderName);
        feature.Provider.Key.Should().Be(editionId);

        features = await featureManager.GetAllForEditionAsync(feature.Provider.Key);
        features.Should().ContainSingle();
        features[0].Should().BeEquivalentTo(feature);

        // when: revoke
        await featureManager.RevokeFromEditionAsync(featureName, editionId);

        // then: revoked
        feature = await featureManager.GetForEditionAsync(featureName, editionId);
        feature.Should().NotBeNull();
        feature.Value.Should().Be("false");
        feature.Name.Should().Be(featureName);
        feature.Provider.Should().NotBeNull();
        feature.Provider.Name.Should().Be(EditionFeatureValueProvider.ProviderName);
        feature.Provider.Key.Should().Be(editionId);
    }

    private static List<FeatureValue> _GetDefaultFeatureValues()
    {
        var defaultProvider = new FeatureValueProvider(DefaultValueFeatureValueProvider.ProviderName, null);

        var defaultFeatureValues = _GroupDefinitions
            .SelectMany(x => x.Features)
            .Select(x => new FeatureValue(x.Name, x.DefaultValue, defaultProvider))
            .ToList();

        return defaultFeatureValues;
    }

    [UsedImplicitly]
    private sealed class Feature1FeaturesDefinitionProvider : IFeatureDefinitionProvider
    {
        public void Define(IFeatureDefinitionContext context)
        {
            context.AddGroup("Group1").AddChild("Feature1", "false");
        }
    }

    [UsedImplicitly]
    private sealed class FeaturesDefinitionProvider : IFeatureDefinitionProvider
    {
        public void Define(IFeatureDefinitionContext context)
        {
            foreach (var item in _GroupDefinitions)
            {
                context.AddGroup(item);
            }
        }
    }
}
