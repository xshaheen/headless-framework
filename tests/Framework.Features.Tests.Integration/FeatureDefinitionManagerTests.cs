// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features;
using Framework.Features.Definitions;
using Framework.Features.Models;
using Framework.Testing.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(FeaturesTestFixture))]
public sealed class FeatureDefinitionManagerTests(FeaturesTestFixture fixture, ITestOutputHelper output)
    : FeaturesTestBase(fixture, output)
{
    private static readonly FeatureGroupDefinition[] _GroupDefinitions =
    [
        TestData.CreateGroupDefinition(4),
        TestData.CreateGroupDefinition(5),
        TestData.CreateGroupDefinition(7),
    ];

    [Fact]
    public async Task should_get_empty_when_call_GetAllAsync_and_no_definitions()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<IFeatureDefinitionManager>();

        // when
        var groups = await definitionManager.GetGroupsAsync();
        var features = await definitionManager.GetFeaturesAsync();

        // then
        groups.Should().BeEmpty();
        features.Should().BeEmpty();
    }

    [Fact]
    public async Task should_get_defined_settings_when_call_GetAllAsync_and_is_defined()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddFeatureDefinitionProvider<FeaturesDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<IFeatureDefinitionManager>();

        // when
        var groups = await definitionManager.GetGroupsAsync();
        var definitions = await definitionManager.GetFeaturesAsync();

        // then
        groups.Should().HaveCount(3);
        groups.Should().BeEquivalentTo(_GroupDefinitions);
        definitions.Should().HaveCount(16);
        definitions.Should().BeEquivalentTo(_GroupDefinitions.SelectMany(x => x.Features));
    }

    [Fact]
    public async Task should_get_default_when_call_GetOrDefaultAsync_and_is_not_defined()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddFeatureDefinitionProvider<FeaturesDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<IFeatureDefinitionManager>();
        var randomSettingName = Test.Faker.Random.String2(5, 10);

        // when
        var definition = await definitionManager.GetOrDefaultAsync(randomSettingName);

        // then
        definition.Should().BeNull();
    }

    [Fact]
    public async Task should_get_defined_setting_when_call_GetOrDefaultAsync_and_is_defined()
    {
        // given
        await Fixture.ResetAsync();
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
            foreach (var item in _GroupDefinitions)
            {
                context.AddGroup(item);
            }
        }
    }
}
