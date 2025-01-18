using Framework.Exceptions;
using Framework.Settings;
using Framework.Settings.Definitions;
using Framework.Settings.Helpers;
using Framework.Settings.Models;
using Framework.Settings.Values;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class SettingManagerTests(SettingsTestFixture fixture, ITestOutputHelper output)
    : SettingsTestBase(fixture, output)
{
    private static readonly List<SettingDefinition> _SettingDefinitions =
    [
        new("Setting1", "Value1", displayName: "Display1"),
        new("Setting2", "Value2", description: "Description2"),
        new("Setting3", "Value3", isInherited: true),
        new("Setting4", "Value4", isVisibleToClients: true, isEncrypted: true),
    ];

    [Fact]
    public async Task should_be_able_to_get_default_value()
    {
        // given
        using var host = CreateHost(b => b.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var settingManager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        // when
        var settingValue = await settingManager.GetDefaultAsync("Setting1");

        // then
        settingValue.Should().Be("Value1");
    }

    [Fact]
    public async Task should_be_able_to_get_all_default_values()
    {
        // given
        using var host = CreateHost(b => b.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var settingManager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        // when
        var settingValues = await settingManager.GetAllDefaultAsync();

        // then
        var expected = _SettingDefinitions.ConvertAll(x => new SettingValue(x.Name, x.DefaultValue));
        settingValues.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task should_throw_error_when_set_not_defined_setting()
    {
        // given
        using var host = CreateHost(b => b.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var settingManager = scope.ServiceProvider.GetRequiredService<ISettingManager>();
        var userId = Guid.NewGuid().ToString();
        const string settingName = "NotDefinedSetting";
        var settingsErrorsProvider = scope.ServiceProvider.GetRequiredService<ISettingsErrorsProvider>();
        var error = await settingsErrorsProvider.DefinitionNotFound(settingName);

        // when
        var act = () => settingManager.SetForUserAsync(userId, name: settingName, value: "NewValue");

        // then
        var assertions = await act.Should().ThrowAsync<ConflictException>();
        var exception = assertions.Which;
        exception.Errors.Should().ContainSingle();
        exception.Errors[0].Should().BeEquivalentTo(error);
    }

    [Fact]
    public async Task should_be_able_to_set_value_when_setting_exist()
    {
        // given
        using var host = CreateHost(b => b.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var settingManager = scope.ServiceProvider.GetRequiredService<ISettingManager>();
        var userId = Guid.NewGuid().ToString();
        const string settingName = "Setting1";

        // when
        await settingManager.SetForUserAsync(userId, name: settingName, value: "NewValue");
        var settingValue = await settingManager.GetForUserAsync(userId, name: settingName);

        // then
        settingValue.Should().Be("NewValue");
    }

    [UsedImplicitly]
    private sealed class SettingsDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            foreach (var settingDefinition in _SettingDefinitions)
            {
                context.Add(settingDefinition);
            }
        }
    }
}
