// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Storage.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class SettingsStorageOptionsTests
{
    [Theory]
    [InlineData("", "SettingValues", "SettingDefinitions")]
    [InlineData("settings", "", "SettingDefinitions")]
    [InlineData("settings", "SettingValues", "")]
    [InlineData("   ", "SettingValues", "SettingDefinitions")]
    [InlineData("settings", "   ", "SettingDefinitions")]
    [InlineData("settings", "SettingValues", "   ")]
    public void should_reject_storage_options_when_any_field_is_blank(
        string schema,
        string valuesTable,
        string definitionsTable
    )
    {
        // given
        var services = new ServiceCollection();
        services.AddSettingsManagementDbContextStorage<SettingsDbContext>(options =>
        {
            options.Schema = schema;
            options.SettingValuesTableName = valuesTable;
            options.SettingDefinitionsTableName = definitionsTable;
        });
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SettingsStorageOptions>>();

        // when
        var act = () => options.Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_accept_storage_options_when_all_fields_are_non_blank()
    {
        // given
        var services = new ServiceCollection();
        services.AddSettingsManagementDbContextStorage<SettingsDbContext>(options =>
        {
            options.Schema = "custom_settings";
            options.SettingValuesTableName = "tbl_setting_values";
            options.SettingDefinitionsTableName = "tbl_setting_definitions";
        });
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SettingsStorageOptions>>();

        // when
        var act = () => options.Value;

        // then
        var resolved = act.Should().NotThrow().Subject;
        resolved.Schema.Should().Be("custom_settings");
        resolved.SettingValuesTableName.Should().Be("tbl_setting_values");
        resolved.SettingDefinitionsTableName.Should().Be("tbl_setting_definitions");
    }

    [Fact]
    public void should_accept_storage_options_when_left_at_defaults()
    {
        // given
        var services = new ServiceCollection();
        services.AddSettingsManagementDbContextStorage<SettingsDbContext>();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SettingsStorageOptions>>();

        // when
        var act = () => options.Value;

        // then
        var resolved = act.Should().NotThrow().Subject;
        resolved.Schema.Should().Be("settings");
        resolved.SettingValuesTableName.Should().Be("SettingValues");
        resolved.SettingDefinitionsTableName.Should().Be("SettingDefinitions");
    }
}
