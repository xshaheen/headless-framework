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
    [Fact]
    public async Task should_provide_setting_value_when_call_get_default_async_and_is_defined()
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
        definitions.Should().ContainSingle(p => p.Name == "some-name");
    }

    private sealed class SettingsDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add(
                new SettingDefinition(
                    name: "some-name",
                    defaultValue: "some-default-value",
                    displayName: "some-display-name",
                    description: "some-description",
                    isVisibleToClients: true,
                    isInherited: true,
                    isEncrypted: true
                )
            );
        }
    }
}
