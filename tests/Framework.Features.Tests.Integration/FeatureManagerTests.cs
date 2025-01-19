using Framework.Core;
using Framework.Exceptions;
using Framework.Features;
using Framework.Features.Definitions;
using Framework.Features.Models;
using Framework.Features.ValueProviders;
using Framework.Features.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    [Fact]
    public async Task should_get_dynamic_features()
    {
        // given: host1 with dynamic feature store enabled
        using var host1 = _CreateDynamicEnabledHostBuilder<Host1FeaturesDefinitionProvider>().Build();
        await using var scope1 = host1.Services.CreateAsyncScope();
        var featureManager1 = scope1.ServiceProvider.GetRequiredService<IFeatureManager>();
        var dynamicStore1 = scope1.ServiceProvider.GetRequiredService<IDynamicFeatureDefinitionStore>();
        const string host1Feature = "Feature1";
        const string host1FeatureValue = "Value1";

        // given: host2 with dynamic feature store enabled
        using var host2 = _CreateDynamicEnabledHostBuilder<Host2FeaturesDefinitionProvider>().Build();
        await using var scope2 = host2.Services.CreateAsyncScope();
        var featureManager2 = scope2.ServiceProvider.GetRequiredService<IFeatureManager>();
        var dynamicStore2 = scope2.ServiceProvider.GetRequiredService<IDynamicFeatureDefinitionStore>();
        const string host2Feature = "Feature2";
        const string host2FeatureValue = "Value2";

        // given: host2 saved its local features to dynamic store
        await dynamicStore2.SaveAsync();

        // when: get dynamic features from host1
        var host1Features = await featureManager1.GetAllDefaultAsync();

        // then: dynamic features should be returned
        host1Features.Should().HaveCount(2);
        host1Features.Should().ContainSingle(x => x.Name == host1Feature && x.Value == host1FeatureValue);
        host1Features.Should().ContainSingle(x => x.Name == host2Feature && x.Value == host2FeatureValue);

        // given: host1 saved its local features to dynamic store
        await dynamicStore1.SaveAsync();

        // when: get dynamic features from host1
        var host2Features = await featureManager1.GetAllDefaultAsync();

        // then: dynamic features should be returned
        host2Features.Should().HaveCount(2);
        host2Features.Should().ContainSingle(x => x.Name == host1Feature && x.Value == host1FeatureValue);
        host2Features.Should().ContainSingle(x => x.Name == host2Feature && x.Value == host2FeatureValue);

        // given
        const string editionId = "AnyEditionId";

        // when: change dynamic feature value in host1
        await featureManager1.GrantToEditionAsync(host2Feature, editionId);

        // then: dynamic feature value should be available in both hosts
        (await featureManager1.GetForEditionAsync(host2Feature, editionId))
            .Value.Should()
            .Be("true");
        (await featureManager2.GetForEditionAsync(host2Feature, editionId)).Value.Should().Be("true");

        // when: change dynamic feature value in host2
        await featureManager2.RevokeFromEditionAsync(host2Feature, editionId);

        // then: dynamic feature value should be changed
        (await featureManager1.GetForEditionAsync(host2Feature, editionId))
            .Value.Should()
            .Be("false");
        (await featureManager2.GetForEditionAsync(host2Feature, editionId)).Value.Should().Be("false");
    }

    private HostApplicationBuilder _CreateDynamicEnabledHostBuilder<T>()
        where T : class, IFeatureDefinitionProvider
    {
        var builder = CreateHostBuilder();

        builder.Services.AddFeatureDefinitionProvider<T>();
        builder.Services.Configure<FeatureManagementOptions>(options => options.IsDynamicFeatureStoreEnabled = true);

        return builder;
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
    private sealed class Host1FeaturesDefinitionProvider : IFeatureDefinitionProvider
    {
        public void Define(IFeatureDefinitionContext context)
        {
            context.AddGroup("Group1").AddChild("Feature1", "Value1");
        }
    }

    [UsedImplicitly]
    private sealed class Host2FeaturesDefinitionProvider : IFeatureDefinitionProvider
    {
        public void Define(IFeatureDefinitionContext context)
        {
            context.AddGroup("Group2").AddChild("Feature2", "Value2");
        }
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
