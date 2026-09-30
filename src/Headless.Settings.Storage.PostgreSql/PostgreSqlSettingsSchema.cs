// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;

namespace Headless.Settings.PostgreSql;

/// <summary>Resolves the settings tables' PostgreSQL names shared by the schema contribution and the repositories.</summary>
internal static class PostgreSqlSettingsSchema
{
    // Resolved from an unconfigured options instance so the defaults stay owned by SettingsStorageOptions.
    public static readonly string DefaultValuesName = ValuesName(new SettingsStorageOptions());
    public static readonly string DefaultDefinitionsName = DefinitionsName(new SettingsStorageOptions());

    /// <summary>Returns the qualified setting values table.</summary>
    public static string ValuesTable(SettingsStorageOptions options)
    {
        return Qualified(options.Schema, ValuesName(options));
    }

    /// <summary>Returns the qualified setting definitions table.</summary>
    public static string DefinitionsTable(SettingsStorageOptions options)
    {
        return Qualified(options.Schema, DefinitionsName(options));
    }

    public static string ValuesName(SettingsStorageOptions options)
    {
        return options.ResolveSettingValuesTableName(StorageNamingStyle.SnakeCase);
    }

    public static string DefinitionsName(SettingsStorageOptions options)
    {
        return options.ResolveSettingDefinitionsTableName(StorageNamingStyle.SnakeCase);
    }

    // Quoted so a configured table name keeps its exact case; the default snake_case names read the same unquoted.
    public static string Qualified(string schema, string tableName)
    {
        return $"""
            "{schema}"."{tableName}"
            """;
    }
}
