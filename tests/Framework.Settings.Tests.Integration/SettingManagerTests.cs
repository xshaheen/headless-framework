using Framework.Exceptions;
using Framework.Settings;
using Framework.Settings.Definitions;
using Framework.Settings.Models;
using Framework.Settings.Repositories;
using Framework.Settings.Resources;
using Framework.Settings.ValueProviders;
using Framework.Settings.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var settingManager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        // when
        var settingValue = await settingManager.FindDefaultAsync("Setting1");

        // then
        settingValue.Should().Be("Value1");
    }

    [Fact]
    public async Task should_get_all_default_values()
    {
        // given
        await Fixture.ResetAsync();
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
        await Fixture.ResetAsync();
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
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var settingManager = scope.ServiceProvider.GetRequiredService<ISettingManager>();
        var userId = Guid.NewGuid().ToString();
        const string settingName = "Setting1";

        // when
        await settingManager.SetForUserAsync(userId, name: settingName, value: "NewValue");
        var settingValue = await settingManager.FindForUserAsync(userId, name: settingName);

        // then
        settingValue.Should().Be("NewValue");
    }

    [Fact]
    public async Task should_set_encrypted_value_when_setting_exist_and_get_it_decrypted()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var settingManager = scope.ServiceProvider.GetRequiredService<ISettingManager>();
        var valueRepository = scope.ServiceProvider.GetRequiredService<ISettingValueRecordRepository>();
        var userId = Guid.NewGuid().ToString();
        const string settingName = "Setting4";

        // when
        await settingManager.SetForUserAsync(userId, name: settingName, value: "NewValue");
        var settingValue = await settingManager.FindForUserAsync(userId, name: settingName);

        // then
        settingValue.Should().Be("NewValue");
        var dbRecord = await valueRepository.FindAsync(settingName, UserSettingValueProvider.ProviderName, userId);
        dbRecord.Should().NotBeNull();
        dbRecord.Name.Should().Be(settingName);
        dbRecord.Value.Should().NotBe("NewValue");
    }

    [Fact]
    public async Task should_get_dynamic_settings()
    {
        // given: host1 with dynamic setting store enabled
        await Fixture.ResetAsync();
        using var host1 = _CreateDynamicEnabledHostBuilder<Host1SettingsDefinitionProvider>().Build();
        await using var scope1 = host1.Services.CreateAsyncScope();
        var settingManager1 = scope1.ServiceProvider.GetRequiredService<ISettingManager>();
        var dynamicStore1 = scope1.ServiceProvider.GetRequiredService<IDynamicSettingDefinitionStore>();
        const string host1Setting = "Setting1";
        const string host1SettingValue = "Value1";

        // given: host2 with dynamic setting store enabled
        using var host2 = _CreateDynamicEnabledHostBuilder<Host2SettingsDefinitionProvider>().Build();
        await using var scope2 = host2.Services.CreateAsyncScope();
        var settingManager2 = scope2.ServiceProvider.GetRequiredService<ISettingManager>();
        var dynamicStore2 = scope2.ServiceProvider.GetRequiredService<IDynamicSettingDefinitionStore>();
        const string host2Setting = "Setting2";
        const string host2SettingValue = "Value2";

        // given: host2 saved its local settings to dynamic store
        await dynamicStore2.SaveAsync();

        // when: get dynamic settings from host1
        var host1LocalSetting = await settingManager1.FindDefaultAsync(host1Setting);
        var host1DynamicSetting = await settingManager1.FindDefaultAsync(host2Setting);

        // then: dynamic settings should be returned
        host1LocalSetting.Should().Be(host1SettingValue);
        host1DynamicSetting.Should().Be(host2SettingValue);

        // given: host1 saved its local settings to dynamic store
        await dynamicStore1.SaveAsync();

        // when: get dynamic settings from host1
        var host2LocalSetting = await settingManager2.FindDefaultAsync(host2Setting);
        var host2DynamicSetting = await settingManager2.FindDefaultAsync(host1Setting);

        // then: dynamic settings should be returned
        host2LocalSetting.Should().Be(host2SettingValue);
        host2DynamicSetting.Should().Be(host1SettingValue);

        // when: change dynamic setting value in host1
        var userId = Guid.NewGuid().ToString();
        await settingManager1.SetForUserAsync(userId, host1Setting, "NewValue1");

        // then: dynamic setting value should be changed

        (await settingManager1.FindForUserAsync(userId, host1Setting))
            .Should()
            .Be("NewValue1");
        (await settingManager2.FindForUserAsync(userId, host1Setting)).Should().Be("NewValue1");

        // when: change dynamic setting value in host2
        await settingManager2.SetForUserAsync(userId, host2Setting, "NewValue2");

        // then: dynamic setting value should be changed
        (await settingManager2.FindForUserAsync(userId, host2Setting))
            .Should()
            .Be("NewValue2");
        (await settingManager1.FindForUserAsync(userId, host2Setting)).Should().Be("NewValue2");
    }

    private HostApplicationBuilder _CreateDynamicEnabledHostBuilder<T>()
        where T : class, ISettingDefinitionProvider
    {
        var builder = CreateHostBuilder();
        builder.Services.AddSettingDefinitionProvider<T>();
        builder.Services.Configure<SettingManagementOptions>(options => options.IsDynamicSettingStoreEnabled = true);

        return builder;
    }

    [UsedImplicitly]
    private sealed class Host1SettingsDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add(new SettingDefinition("Setting1", "Value1"));
        }
    }

    [UsedImplicitly]
    private sealed class Host2SettingsDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add(new SettingDefinition("Setting2", "Value2"));
        }
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
