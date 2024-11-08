// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features;
using Framework.Features.Definitions;
using Framework.Features.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(FeaturesTestFixture))]
public sealed class DynamicFeatureDefinitionStoreTests(FeaturesTestFixture fixture)
{
    [Fact]
    public async Task should_save_defined_settings_when_call_SaveAsync()
    {
        // given
        var hostBuilder = _CreateFeaturesHostBuilder();

        hostBuilder.Services.Configure<FeatureManagementOptions>(options =>
        {
            options.IsDynamicFeatureStoreEnabled = true;
            options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.Zero;
        });

        var host = hostBuilder.Build();

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

    private HostApplicationBuilder _CreateFeaturesHostBuilder()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddFeatureDefinitionProvider<FeaturesDefinitionProvider>();
        builder.Services.ConfigureFeaturesServices(fixture.ConnectionString);

        return builder;
    }
}
