// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings;
using Framework.Settings.Definitions;
using Framework.Settings.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(SettingsTestFixture))]
public sealed class SettingDefinitionManagerTests(SettingsTestFixture fixture)
{
    private static readonly SettingDefinition _SettingDefinition = TestData.CreateSettingDefinitionFaker().Generate();

    [Fact]
    public async Task should_get_defined_settings_when_call_GetAllAsync_and_is_defined()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>();
        builder.Services.ConfigureServices(fixture.ConnectionString);
        var host = builder.Build();

        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<ISettingDefinitionManager>();

        // when
        var definitions = await definitionManager.GetAllAsync();

        // then
        definitions.Should().NotBeEmpty();
        definitions.Should().Contain(_SettingDefinition);
    }

    [Fact]
    public async Task should_get_defined_setting_when_call_GetOrDefaultAsync_and_is_defined()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>();
        builder.Services.ConfigureServices(fixture.ConnectionString);
        var host = builder.Build();

        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<ISettingDefinitionManager>();

        // when
        var definition = await definitionManager.GetOrDefaultAsync(_SettingDefinition.Name);

        // then
        definition.Should().NotBeNull();
        definition!.Should().Be(_SettingDefinition);
    }

    [UsedImplicitly]
    private sealed class SettingsDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add(_SettingDefinition);
        }
    }
}
