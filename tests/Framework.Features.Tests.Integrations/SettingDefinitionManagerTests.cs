// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features;
using Framework.Features.Definitions;
using Framework.Features.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(SettingsTestFixture))]
public sealed class SettingDefinitionManagerTests(SettingsTestFixture fixture)
{
    [Fact]
    public async Task should_provide_setting_value_when_call_get_default_async_and_is_defined()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddFeatureDefinitionProvider<FeatureDefinitionProvider>();
        builder.Services.ConfigureServices(fixture.ConnectionString);
        var host = builder.Build();

        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<IFeatureDefinitionManager>();

        // when
        var groups = await definitionManager.GetAllGroupsAsync();
        var features = await definitionManager.GetAllFeaturesAsync();

        // then
        groups.Should().NotBeEmpty();
        groups.Should().ContainSingle(p => p.Name == "some-group");
        features.Should().NotBeEmpty();
        features.Should().ContainSingle(p => p.Name == "some-feature");
    }

    [UsedImplicitly]
    private sealed class FeatureDefinitionProvider : IFeatureDefinitionProvider
    {
        public void Define(IFeatureDefinitionContext context)
        {
            var group = context.AddGroup("some-group");

            group.AddChild("some-feature", "some-display-name", "some-description");
        }
    }
}
