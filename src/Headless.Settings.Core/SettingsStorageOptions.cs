// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings;

[PublicAPI]
public sealed class SettingsStorageOptions
{
    public string Schema { get; set; } = "settings";

    public string SettingValuesTableName { get; set; } = "SettingValues";

    public string SettingDefinitionsTableName { get; set; } = "SettingDefinitions";

    /// <summary>
    /// Copies every property to <paramref name="target"/>. Centralizes the property list so
    /// adding a new property to this type only requires extending this single method — the
    /// setup pipeline picks it up automatically instead of silently dropping it.
    /// </summary>
    internal void CopyTo(SettingsStorageOptions target)
    {
        target.Schema = Schema;
        target.SettingValuesTableName = SettingValuesTableName;
        target.SettingDefinitionsTableName = SettingDefinitionsTableName;
    }
}
