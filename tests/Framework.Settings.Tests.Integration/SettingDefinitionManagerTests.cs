// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings;
using Framework.Settings.Definitions;
using Framework.Settings.Models;
using Framework.Testing.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class SettingDefinitionManagerTests(SettingsTestFixture fixture, ITestOutputHelper output)
    : SettingsTestBase(fixture, output)
{
    private static readonly List<SettingDefinition> _Definitions = TestData.CreateDefinitionFaker().Generate(5);

    [Fact]
    public async Task should_get_empty_when_call_GetAllAsync_and_no_definitions()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<ISettingDefinitionManager>();

        // when
        var definitions = await definitionManager.GetAllAsync();

        // then
        definitions.Should().BeEmpty();
    }

    [Fact]
    public async Task should_get_defined_settings_when_call_GetAllAsync_and_is_defined()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<ISettingDefinitionManager>();

        // when
        var definitions = await definitionManager.GetAllAsync();

        // then
        definitions.Should().NotBeEmpty();
        definitions.Should().HaveCount(_Definitions.Count);
        definitions.Should().BeEquivalentTo(_Definitions);
    }

    [Fact]
    public async Task should_get_defined_setting_when_call_GetOrDefaultAsync_and_is_defined()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<ISettingDefinitionManager>();
        var oneOfTheDefinedSetting = _Definitions[0];

        // when
        var definition = await definitionManager.FindAsync(oneOfTheDefinedSetting.Name);

        // then
        definition.Should().NotBeNull();
        definition.Should().Be(oneOfTheDefinedSetting);
    }

    [Fact]
    public async Task should_get_default_when_call_GetOrDefaultAsync_and_is_not_defined()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<ISettingDefinitionManager>();
        var randomSettingName = Faker.Random.String2(5, 10);

        // when
        var definition = await definitionManager.FindAsync(randomSettingName);

        // then
        definition.Should().BeNull();
    }

    [UsedImplicitly]
    private sealed class SettingsDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            foreach (var settingDefinition in _Definitions)
            {
                context.Add(settingDefinition);
            }
        }
    }
}
