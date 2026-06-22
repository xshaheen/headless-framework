// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings;

/// <summary>Configuration options for the settings storage layer (schema name, table names, and startup behaviour).</summary>
[PublicAPI]
public sealed class SettingsStorageOptions
{
    /// <summary>Gets or sets the database schema that contains the settings tables. Defaults to <c>settings</c>.</summary>
    public string Schema { get; set; } = "settings";

    /// <summary>Gets or sets the name of the table that stores setting values. Defaults to <c>SettingValues</c>.</summary>
    public string SettingValuesTableName { get; set; } = "SettingValues";

    /// <summary>Gets or sets the name of the table that stores setting definitions. Defaults to <c>SettingDefinitions</c>.</summary>
    public string SettingDefinitionsTableName { get; set; } = "SettingDefinitions";

    /// <summary>
    /// When false, the startup storage initializer is skipped (no-op) — use when the schema is
    /// provisioned out-of-band (migrations job / DBA). The initializer still reports
    /// IsInitialized=true so dependents that await WaitForInitializationAsync do not block. Only
    /// affects raw-DDL self-initializing providers; EF-mode storage uses migrations.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

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
        target.InitializeOnStartup = InitializeOnStartup;
    }
}
