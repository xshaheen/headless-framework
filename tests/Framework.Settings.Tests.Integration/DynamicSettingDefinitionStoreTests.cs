// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings;
using Framework.Settings.Definitions;
using Framework.Settings.Models;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class DynamicSettingDefinitionStoreTests(SettingsTestFixture fixture, ITestOutputHelper output)
    : SettingsTestBase(fixture, output)
{
    private static readonly List<SettingDefinition> _SettingDefinitions = TestData.CreateDefinitionFaker().Generate(10);

    [Fact]
    public async Task should_save_defined_settings()
    {
        // given
        await Fixture.ResetAsync();
        var builder = CreateHostBuilder();

        builder.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>();

        builder.Services.Configure<SettingManagementOptions>(options =>
        {
            options.IsDynamicSettingStoreEnabled = true;
            options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.Zero;
        });

        using var host = builder.Build();

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
            context.Add(_SettingDefinitions.AsSpan());
        }
    }
}
