// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Tests.Models;

public sealed class SettingValueTests
{
    [Fact]
    public void should_create_with_name_and_value()
    {
        // given
        const string name = "App.Theme";
        const string value = "dark";

        // when
        var settingValue = new SettingValue(name, value);

        // then
        settingValue.Name.Should().Be(name);
        settingValue.Value.Should().Be(value);
        settingValue.Provider.Should().BeNull();
    }

    [Fact]
    public void should_allow_null_value()
    {
        // given
        const string name = "App.Theme";

        // when
        var settingValue = new SettingValue(name, null);

        // then
        settingValue.Name.Should().Be(name);
        settingValue.Value.Should().BeNull();
        settingValue.Provider.Should().BeNull();
    }

    [Fact]
    public void should_carry_provider_attribution()
    {
        // given
        const string name = "App.Language";

        // when
        var settingValue = new SettingValue(name, "en", new SettingValueProvider("Tenant", "tenant-1"));

        // then
        settingValue.Name.Should().Be(name);
        settingValue.Value.Should().Be("en");
        settingValue.Provider.Should().NotBeNull();
        settingValue.Provider!.Name.Should().Be("Tenant");
        settingValue.Provider.Key.Should().Be("tenant-1");
    }

    [Fact]
    public void should_compare_by_value()
    {
        // given
        var provider = new SettingValueProvider("Global", Key: null);

        // when
        var a = new SettingValue("App.Theme", "light", provider);
        var b = new SettingValue("App.Theme", "light", provider);

        // then
        a.Should().Be(b);
    }
}
