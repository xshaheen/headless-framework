// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;

namespace Headless.Permissions.PostgreSql;

/// <summary>Table names of the permissions store, shared by the schema contribution's DDL and the repositories' statements.</summary>
internal static class PostgreSqlPermissionsSchema
{
    public static string GrantsName(PermissionsStorageOptions options)
    {
        return options.ResolvePermissionGrantsTableName(StorageNamingStyle.SnakeCase);
    }

    public static string DefinitionsName(PermissionsStorageOptions options)
    {
        return options.ResolvePermissionDefinitionsTableName(StorageNamingStyle.SnakeCase);
    }

    public static string GroupsName(PermissionsStorageOptions options)
    {
        return options.ResolvePermissionGroupDefinitionsTableName(StorageNamingStyle.SnakeCase);
    }

    /// <summary>Returns the qualified permission grants table.</summary>
    public static string GrantsTable(PermissionsStorageOptions options)
    {
        return Qualified(options.Schema, GrantsName(options));
    }

    /// <summary>Returns the qualified permission definitions table.</summary>
    public static string DefinitionsTable(PermissionsStorageOptions options)
    {
        return Qualified(options.Schema, DefinitionsName(options));
    }

    /// <summary>Returns the qualified permission group definitions table.</summary>
    public static string GroupsTable(PermissionsStorageOptions options)
    {
        return Qualified(options.Schema, GroupsName(options));
    }

    // Quoted so a configured table name keeps its exact case; the default snake_case names read the same unquoted.
    public static string Qualified(string schema, string tableName)
    {
        return $"""
            "{schema}"."{tableName}"
            """;
    }
}
