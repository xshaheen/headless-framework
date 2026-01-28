// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Features.Definitions;
using Headless.Features.Models;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class DynamicFeatureDefinitionStoreTests(FeaturesTestFixture fixture) : FeaturesTestBase(fixture)
{
    private static readonly FeatureGroupDefinition _GroupDefinition = TestData.CreateGroupDefinition();

    [Fact]
    public async Task should_save_defined_features()
    {
        // given
        await Fixture.ResetAsync();
        var builder = CreateHostBuilder();
        builder.Services.AddFeatureDefinitionProvider<FeaturesDefinitionProvider>();

        builder.Services.Configure<FeatureManagementOptions>(options =>
        {
            options.IsDynamicFeatureStoreEnabled = true;
            options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.Zero;
        });

        using var host = builder.Build();

        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDynamicFeatureDefinitionStore>();
        var groupsBefore = await store.GetGroupsAsync(AbortToken);
        var definitionsBefore = await store.GetFeaturesAsync(AbortToken);

        // when
        await store.SaveAsync(AbortToken);
        var definitionsAfter = await store.GetFeaturesAsync(AbortToken);
        var groupsAfter = await store.GetGroupsAsync(AbortToken);

        // then
        definitionsBefore.Should().BeEmpty();
        groupsBefore.Should().BeEmpty();
        definitionsAfter.Should().HaveCount(3);
        groupsAfter.Should().ContainSingle();
        groupsAfter[0].Should().BeEquivalentTo(_GroupDefinition);
    }

    [UsedImplicitly]
    private sealed class FeaturesDefinitionProvider : IFeatureDefinitionProvider
    {
        public void Define(IFeatureDefinitionContext context) => context.AddGroup(_GroupDefinition);
    }
}
