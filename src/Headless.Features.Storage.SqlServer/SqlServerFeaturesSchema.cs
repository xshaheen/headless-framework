// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;

namespace Headless.Features.SqlServer;

/// <summary>Table names of the features store, shared by the schema contribution's DDL and the repositories' statements.</summary>
internal static class SqlServerFeaturesSchema
{
    /// <summary>Returns the fully-qualified <c>[schema].[table]</c> identifier for <paramref name="tableName"/>.</summary>
    /// <param name="options">Storage options supplying the schema name.</param>
    /// <param name="tableName">Unqualified table name.</param>
    /// <returns>A bracket-quoted, schema-qualified table identifier safe for SQL Server DDL/DML.</returns>
    public static string Qualified(FeaturesStorageOptions options, string tableName)
    {
        return $"[{options.Schema}].[{tableName}]";
    }

    /// <summary>Returns the qualified feature values table.</summary>
    public static string ValuesTable(FeaturesStorageOptions options)
    {
        return Qualified(options, options.ResolveFeatureValuesTableName(StorageNamingStyle.PascalCase));
    }

    /// <summary>Returns the qualified feature definitions table.</summary>
    public static string DefinitionsTable(FeaturesStorageOptions options)
    {
        return Qualified(options, options.ResolveFeatureDefinitionsTableName(StorageNamingStyle.PascalCase));
    }

    /// <summary>Returns the qualified feature group definitions table.</summary>
    public static string GroupsTable(FeaturesStorageOptions options)
    {
        return Qualified(options, options.ResolveFeatureGroupDefinitionsTableName(StorageNamingStyle.PascalCase));
    }
}
