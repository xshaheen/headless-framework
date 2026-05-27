// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings;

[PublicAPI]
public sealed class SettingsStorageOptions
{
    public string Schema { get; set; } = "settings";

    public string SettingValuesTableName { get; set; } = "SettingValues";

    public string SettingDefinitionsTableName { get; set; } = "SettingDefinitions";
}
