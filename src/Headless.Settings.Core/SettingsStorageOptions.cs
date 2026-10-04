// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Headless.Settings;

/// <summary>Configuration options for the settings storage layer (schema name, table names, and startup behaviour).</summary>
[PublicAPI]
public sealed class SettingsStorageOptions
{
    /// <summary>
    /// Gets or sets the database schema that contains the settings tables. Defaults to
    /// <see cref="HeadlessStorageDefaults.Schema"/> (<c>headless</c>), the schema every Headless feature shares.
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;

    /// <summary>
    /// Gets or sets the name of the table that stores setting values. Default: <see langword="null"/>, which uses the
    /// database's conventional name: <c>setting_values</c> on PostgreSQL, <c>SettingValues</c> elsewhere. A configured
    /// name is used verbatim, and the table's key and index names derive from it.
    /// </summary>
    public string? SettingValuesTableName { get; set; }

    /// <summary>
    /// Gets or sets the name of the table that stores setting definitions. Default: <see langword="null"/>, which uses
    /// the database's conventional name: <c>setting_definitions</c> on PostgreSQL, <c>SettingDefinitions</c> elsewhere.
    /// A configured name is used verbatim.
    /// </summary>
    public string? SettingDefinitionsTableName { get; set; }

    /// <summary>
    /// When false, the startup storage initializer is skipped (no-op) — use when the schema is
    /// provisioned out-of-band (migrations job / DBA). The initializer still reports
    /// IsInitialized=true so dependents that await WaitForInitializationAsync do not block. Only
    /// affects raw-DDL self-initializing providers; EF-mode storage uses migrations.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    /// <summary>Returns the setting values table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolveSettingValuesTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(SettingValuesTableName, style, "SettingValues");
    }

    /// <summary>Returns the setting definitions table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolveSettingDefinitionsTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(SettingDefinitionsTableName, style, "SettingDefinitions");
    }

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
