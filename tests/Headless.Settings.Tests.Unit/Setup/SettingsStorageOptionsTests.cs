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
    public void should_validate_storage_option_fields(string schema, string valuesTable, string definitionsTable)
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
}
