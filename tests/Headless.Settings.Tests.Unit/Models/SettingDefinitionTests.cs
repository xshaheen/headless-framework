// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Tests.Models;

public sealed class SettingDefinitionTests
{
    [Fact]
    public void should_create_with_required_name()
    {
        // given
        const string name = "App.Theme";

        // when
        var definition = new SettingDefinition(name);

        // then
        definition.Name.Should().Be(name);
    }

    [Fact]
    public void should_store_default_value()
    {
        // given
        const string name = "App.Theme";
        const string defaultValue = "dark";

        // when
        var definition = new SettingDefinition(name, defaultValue: defaultValue);

        // then
        definition.DefaultValue.Should().Be(defaultValue);
    }

    [Fact]
    public void should_store_display_name()
    {
        // given
        const string name = "App.Theme";
        const string displayName = "Application Theme";

        // when
        var definition = new SettingDefinition(name, displayName: displayName);

        // then
        definition.DisplayName.Should().Be(displayName);
    }

    [Fact]
    public void should_store_description()
    {
        // given
        const string name = "App.Theme";
        const string description = "The application theme setting";

        // when
        var definition = new SettingDefinition(name, description: description);

        // then
        definition.Description.Should().Be(description);
    }

    [Fact]
    public void should_store_is_encrypted_flag()
    {
        // given
        const string name = "App.Secret";

        // when
        var definition = new SettingDefinition(name, isEncrypted: true);

        // then
        definition.IsEncrypted.Should().BeTrue();
    }

    [Fact]
    public void should_store_is_inherited_flag()
    {
        // given
        const string name = "App.Theme";

        // when
        var definition = new SettingDefinition(name, isInherited: false);

        // then
        definition.IsInherited.Should().BeFalse();
    }

    [Fact]
    public void should_store_providers_list()
    {
        // given
        const string name = "App.Theme";
        var definition = new SettingDefinition(name);

        // when
        definition.Providers.Add("Global");
        definition.Providers.Add("Tenant");

        // then
        definition.Providers.Should().HaveCount(2);
        definition.Providers.Should().Contain("Global");
        definition.Providers.Should().Contain("Tenant");
    }

    [Fact]
    public void should_store_custom_properties()
    {
        // given
        const string name = "App.Theme";
        var definition = new SettingDefinition(name);

        // when
        definition["CustomKey"] = "CustomValue";
        definition.Properties["AnotherKey"] = 123;

        // then
        definition["CustomKey"].Should().Be("CustomValue");
        definition.Properties["AnotherKey"].Should().Be(123);
    }

    [Fact]
    public void should_default_is_encrypted_to_false()
    {
        // given
        const string name = "App.Theme";

        // when
        var definition = new SettingDefinition(name);

        // then
        definition.IsEncrypted.Should().BeFalse();
    }

    [Fact]
    public void should_default_is_inherited_to_true()
    {
        // given
        const string name = "App.Theme";

        // when
        var definition = new SettingDefinition(name);

        // then
        definition.IsInherited.Should().BeTrue();
    }
}
