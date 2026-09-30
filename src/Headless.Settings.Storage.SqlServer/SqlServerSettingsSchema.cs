// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;

namespace Headless.Settings.SqlServer;

/// <summary>Resolves the settings tables' SQL Server names shared by the schema contribution and the repositories.</summary>
internal static class SqlServerSettingsSchema
{
    // Resolved from an unconfigured options instance so the defaults stay owned by SettingsStorageOptions.
    public static readonly string DefaultValuesName = ValuesName(new SettingsStorageOptions());
    public static readonly string DefaultDefinitionsName = DefinitionsName(new SettingsStorageOptions());

    /// <summary>Returns the bracket-quoted <c>[schema].[table]</c> identifier for <paramref name="tableName"/>.</summary>
    public static string Qualified(SettingsStorageOptions options, string tableName)
    {
        return $"[{options.Schema}].[{tableName}]";
    }

    /// <summary>Returns the qualified setting values table.</summary>
    public static string ValuesTable(SettingsStorageOptions options)
    {
        return Qualified(options, ValuesName(options));
    }

    /// <summary>Returns the qualified setting definitions table.</summary>
    public static string DefinitionsTable(SettingsStorageOptions options)
    {
        return Qualified(options, DefinitionsName(options));
    }

    public static string ValuesName(SettingsStorageOptions options)
    {
        return options.ResolveSettingValuesTableName(StorageNamingStyle.PascalCase);
    }

    public static string DefinitionsName(SettingsStorageOptions options)
    {
        return options.ResolveSettingDefinitionsTableName(StorageNamingStyle.PascalCase);
    }
}
