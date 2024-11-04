// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings;
using Framework.Settings.Definitions;
using Framework.Settings.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(SettingsTestFixture))]
public sealed class DynamicSettingDefinitionStoreTests(SettingsTestFixture fixture)
{
    private readonly SettingDefinition _settingDefinition = TestData.CreateSettingDefinitionFaker().Generate();

    [Fact]
    public async Task should_save_defined_settings_when_call_SaveAsync()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>();
        builder.Services.ConfigureServices(fixture.ConnectionString);
        var host = builder.Build();

        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<ISettingDefinitionManager>();

        // when
        var definition = await definitionManager.GetOrDefaultAsync("some-name");

        // then
        definition.Should().NotBeNull();
        definition!.Name.Should().Be("some-name");
    }

    [UsedImplicitly]
    private sealed class SettingsDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add(TestData.CreateSettingDefinitionFaker());
        }
    }
}
