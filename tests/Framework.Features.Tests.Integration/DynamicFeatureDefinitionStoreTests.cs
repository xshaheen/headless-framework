// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features;
using Framework.Features.Definitions;
using Framework.Features.Models;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class DynamicFeatureDefinitionStoreTests(FeaturesTestFixture fixture, ITestOutputHelper output)
    : FeaturesTestBase(fixture, output)
{
    [Fact]
    public async Task should_save_defined_settings_when_call_SaveAsync()
    {
        // given
        var builder = CreateHostBuilder();
        builder.Services.AddFeatureDefinitionProvider<FeaturesDefinitionProvider>();

        builder.Services.Configure<FeatureManagementOptions>(options =>
        {
            options.IsDynamicFeatureStoreEnabled = true;
            options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.Zero;
        });

        var host = builder.Build();

        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDynamicFeatureDefinitionStore>();
        var groupsBefore = await store.GetGroupsAsync();
        var definitionsBefore = await store.GetFeaturesAsync();

        // when
        await store.SaveAsync();
        var definitionsAfter = await store.GetFeaturesAsync();
        var groupsAfter = await store.GetGroupsAsync();

        // then
        definitionsBefore.Should().BeEmpty();
        groupsBefore.Should().BeEmpty();

        definitionsAfter.Should().NotBeEmpty();
        definitionsAfter.Should().HaveCount(3);

        groupsAfter.Should().NotBeEmpty();
        groupsAfter.Should().ContainSingle();
    }

    [UsedImplicitly]
    private sealed class FeaturesDefinitionProvider : IFeatureDefinitionProvider
    {
        public void Define(IFeatureDefinitionContext context)
        {
            var group = context.AddGeneratedFeatureGroup();
            group.AddGeneratedFeatureDefinition();
            group.AddGeneratedFeatureDefinition();
            group.AddGeneratedFeatureDefinition();
        }
    }
}
