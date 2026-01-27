// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Models;

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
    }

    [Fact]
    public void should_store_name_property()
    {
        // given
        const string name = "App.Language";

        // when
        var settingValue = new SettingValue(name);

        // then
        settingValue.Name.Should().Be(name);
    }

    [Fact]
    public void should_store_value_property()
    {
        // given
        const string name = "App.Theme";
        var settingValue = new SettingValue(name);

        // when
        settingValue.Value = "light";

        // then
        settingValue.Value.Should().Be("light");
    }
}
