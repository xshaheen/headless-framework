using Framework.Exceptions;
using Framework.Settings;
using Framework.Settings.Definitions;
using Framework.Settings.Models;
using Framework.Settings.Resources;
using Framework.Settings.ValueProviders;
using Framework.Settings.Values;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class SettingManagerTests(SettingsTestFixture fixture, ITestOutputHelper output)
    : SettingsTestBase(fixture, output)
{
    private static readonly List<SettingDefinition> _Definitions =
    [
        new("Setting1", "Value1", displayName: "Display1"),
        new("Setting2", "Value2", description: "Description2"),
        new("Setting3", "Value3", isInherited: true),
        new("Setting4", "Value4", isVisibleToClients: true, isEncrypted: true),
    ];

    [Fact]
    public async Task should_get_default_value()
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
    public async Task should_get_all_default_values()
    {
        // given
        using var host = CreateHost(b => b.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var settingManager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        // when
        var settingValues = await settingManager.GetAllDefaultAsync();

        // then
        var expected = _Definitions.ConvertAll(x => new SettingValue(x.Name, x.DefaultValue));
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
        var settingsErrorsProvider = scope.ServiceProvider.GetRequiredService<ISettingsErrorsDescriptor>();
        var error = await settingsErrorsProvider.NotDefined(settingName);

        // when
        var act = () => settingManager.SetForUserAsync(userId, name: settingName, value: "NewValue");

        // then
        var assertions = await act.Should().ThrowAsync<ConflictException>();
        var exception = assertions.Which;
        exception.Errors.Should().ContainSingle();
        exception.Errors[0].Should().BeEquivalentTo(error);
    }

    [Fact]
    public async Task should_set_value_when_setting_exist()
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

    [Fact]
    public async Task should_set_encrypted_value_when_setting_exist_and_get_it_decrypted()
    {
        // given
        using var host = CreateHost(b => b.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var settingManager = scope.ServiceProvider.GetRequiredService<ISettingManager>();
        var valueRepository = scope.ServiceProvider.GetRequiredService<ISettingValueRecordRepository>();
        var userId = Guid.NewGuid().ToString();
        const string settingName = "Setting4";

        // when
        await settingManager.SetForUserAsync(userId, name: settingName, value: "NewValue");
        var settingValue = await settingManager.GetForUserAsync(userId, name: settingName);

        // then
        settingValue.Should().Be("NewValue");
        var dbRecord = await valueRepository.FindAsync(settingName, UserSettingValueProvider.ProviderName, userId);
        dbRecord.Should().NotBeNull();
        dbRecord.Name.Should().Be(settingName);
        dbRecord.Value.Should().NotBe("NewValue");
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
