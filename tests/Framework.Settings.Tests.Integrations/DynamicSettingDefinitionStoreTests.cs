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
    private static readonly List<SettingDefinition> _SettingDefinitions = TestData
        .CreateSettingDefinitionFaker()
        .Generate(10);

    [Fact]
    public async Task should_save_defined_settings_when_call_SaveAsync()
    {
        // given
        var host = _CreateSettingsHost();

        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDynamicSettingDefinitionStore>();
        var beforeDefinitions = await store.GetAllAsync();

        // when
        await store.SaveAsync();
        var afterDefinitions = await store.GetAllAsync();

        // then
        beforeDefinitions.Should().BeEmpty();
        afterDefinitions.Should().NotBeEmpty();
        afterDefinitions.Should().BeEquivalentTo(_SettingDefinitions);
    }

    [UsedImplicitly]
    private sealed class SettingsDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add([.. _SettingDefinitions]);
        }
    }

    private IHost _CreateSettingsHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>();
        builder.Services.ConfigureSettingsServices(fixture.ConnectionString);
        builder.Services.Configure<SettingManagementOptions>(options =>
        {
            options.SaveStaticSettingsToDatabase = false;
            options.IsDynamicSettingStoreEnabled = true;
        });
        var host = builder.Build();

        return host;
    }
}
