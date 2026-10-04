// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Headless.Features;

/// <summary>Storage-layer configuration shared across all feature-management database providers.</summary>
[PublicAPI]
public sealed class FeaturesStorageOptions
{
    /// <summary>
    /// Gets or sets the database schema that contains the features tables. Default:
    /// <see cref="HeadlessStorageDefaults.Schema"/> (<c>"headless"</c>), the schema every Headless feature shares.
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;

    /// <summary>
    /// Gets or sets the name of the table that stores per-provider feature values. Default: <see langword="null"/>,
    /// which uses the database's conventional name: <c>feature_values</c> on PostgreSQL, <c>FeatureValues</c>
    /// elsewhere. A configured name is used verbatim, and the table's key and index names derive from it.
    /// </summary>
    public string? FeatureValuesTableName { get; set; }

    /// <summary>
    /// Gets or sets the name of the table that stores feature definitions. Default: <see langword="null"/>, which uses
    /// the database's conventional name: <c>feature_definitions</c> on PostgreSQL, <c>FeatureDefinitions</c>
    /// elsewhere. A configured name is used verbatim.
    /// </summary>
    public string? FeatureDefinitionsTableName { get; set; }

    /// <summary>
    /// Gets or sets the name of the table that stores feature group definitions. Default: <see langword="null"/>,
    /// which uses the database's conventional name: <c>feature_group_definitions</c> on PostgreSQL,
    /// <c>FeatureGroupDefinitions</c> elsewhere. A configured name is used verbatim.
    /// </summary>
    public string? FeatureGroupDefinitionsTableName { get; set; }

    /// <summary>
    /// When <see langword="true"/> (default), the startup storage initializer creates the schema, tables, and indexes
    /// on first run. Set to <see langword="false"/> when the schema is provisioned out-of-band (e.g., by a DBA or
    /// a migrations job); the initializer becomes a no-op but still signals completion so callers that await
    /// <c>WaitForInitializationAsync</c> do not block. Applies only to raw-DDL self-initializing providers
    /// (PostgreSQL, SQL Server); EF Core storage is always schema-managed by migrations.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    /// <summary>Returns the feature values table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolveFeatureValuesTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(FeatureValuesTableName, style, "FeatureValues");
    }

    /// <summary>Returns the feature definitions table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolveFeatureDefinitionsTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(FeatureDefinitionsTableName, style, "FeatureDefinitions");
    }

    /// <summary>Returns the feature group definitions table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolveFeatureGroupDefinitionsTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(FeatureGroupDefinitionsTableName, style, "FeatureGroupDefinitions");
    }

    /// <summary>
    /// Copies every property to <paramref name="target"/>. Centralizes the property list so
    /// adding a new property to this type only requires extending this single method — the
    /// setup pipeline picks it up automatically instead of silently dropping it.
    /// </summary>
    internal void CopyTo(FeaturesStorageOptions target)
    {
        target.Schema = Schema;
        target.FeatureValuesTableName = FeatureValuesTableName;
        target.FeatureDefinitionsTableName = FeatureDefinitionsTableName;
        target.FeatureGroupDefinitionsTableName = FeatureGroupDefinitionsTableName;
        target.InitializeOnStartup = InitializeOnStartup;
    }
}
