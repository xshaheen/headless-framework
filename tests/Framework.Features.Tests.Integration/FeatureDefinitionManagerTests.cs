// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features;
using Framework.Features.Definitions;
using Framework.Features.Models;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(FeaturesTestFixture))]
public sealed class FeatureDefinitionManagerTests(FeaturesTestFixture fixture, ITestOutputHelper output)
    : FeaturesTestBase(fixture, output)
{
    [Fact]
    public async Task should_get_defined_settings_when_call_GetAllAsync_and_is_defined()
    {
        // given
        using var host = CreateHost(b => b.Services.AddFeatureDefinitionProvider<FeaturesDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<IFeatureDefinitionManager>();

        // when
        var groups = await definitionManager.GetGroupsAsync();
        var definitions = await definitionManager.GetFeaturesAsync();

        // then
        definitions.Should().NotBeEmpty();
        groups.Should().NotBeEmpty();
        groups.Should().ContainSingle();
        definitions.Should().HaveCount(3);
    }

    [Fact]
    public async Task should_get_defined_setting_when_call_GetOrDefaultAsync_and_is_defined()
    {
        // given
        using var host = CreateHost(b => b.Services.AddFeatureDefinitionProvider<FeaturesDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<IFeatureDefinitionManager>();
        var definitions = await definitionManager.GetFeaturesAsync();
        var existDefinition = definitions[0];

        // when
        var definition = await definitionManager.GetOrDefaultAsync(existDefinition.Name);

        // then
        definition.Should().NotBeNull();
        definition.Name.Should().Be(existDefinition.Name);
        definition.DisplayName.Should().Be(existDefinition.DisplayName);
        definition.Description.Should().Be(existDefinition.Description);
        definition.IsAvailableToHost.Should().Be(existDefinition.IsAvailableToHost);
        definition.IsVisibleToClients.Should().Be(existDefinition.IsVisibleToClients);
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
