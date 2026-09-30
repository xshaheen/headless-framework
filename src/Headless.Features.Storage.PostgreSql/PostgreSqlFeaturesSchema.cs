// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;

namespace Headless.Features.PostgreSql;

/// <summary>Table names of the features store, shared by the schema contribution's DDL and the repositories' statements.</summary>
internal static class PostgreSqlFeaturesSchema
{
    public static string ValuesName(FeaturesStorageOptions options)
    {
        return options.ResolveFeatureValuesTableName(StorageNamingStyle.SnakeCase);
    }

    public static string DefinitionsName(FeaturesStorageOptions options)
    {
        return options.ResolveFeatureDefinitionsTableName(StorageNamingStyle.SnakeCase);
    }

    public static string GroupsName(FeaturesStorageOptions options)
    {
        return options.ResolveFeatureGroupDefinitionsTableName(StorageNamingStyle.SnakeCase);
    }

    /// <summary>Returns the qualified feature values table.</summary>
    public static string ValuesTable(FeaturesStorageOptions options)
    {
        return Qualified(options.Schema, ValuesName(options));
    }

    /// <summary>Returns the qualified feature definitions table.</summary>
    public static string DefinitionsTable(FeaturesStorageOptions options)
    {
        return Qualified(options.Schema, DefinitionsName(options));
    }

    /// <summary>Returns the qualified feature group definitions table.</summary>
    public static string GroupsTable(FeaturesStorageOptions options)
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
