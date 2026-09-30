// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;

namespace Headless.Permissions.SqlServer;

/// <summary>Table names of the permissions store, shared by the schema contribution's DDL and the repositories' statements.</summary>
internal static class SqlServerPermissionsSchema
{
    public static string Qualified(PermissionsStorageOptions options, string tableName)
    {
        return $"[{options.Schema}].[{tableName}]";
    }

    /// <summary>Returns the qualified permission grants table.</summary>
    public static string GrantsTable(PermissionsStorageOptions options)
    {
        return Qualified(options, options.ResolvePermissionGrantsTableName(StorageNamingStyle.PascalCase));
    }

    /// <summary>Returns the qualified permission definitions table.</summary>
    public static string DefinitionsTable(PermissionsStorageOptions options)
    {
        return Qualified(options, options.ResolvePermissionDefinitionsTableName(StorageNamingStyle.PascalCase));
    }

    /// <summary>Returns the qualified permission group definitions table.</summary>
    public static string GroupsTable(PermissionsStorageOptions options)
    {
        return Qualified(options, options.ResolvePermissionGroupDefinitionsTableName(StorageNamingStyle.PascalCase));
    }
}
