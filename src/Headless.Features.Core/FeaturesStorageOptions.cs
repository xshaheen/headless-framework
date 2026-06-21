// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features;

/// <summary>Storage-layer configuration shared across all feature-management database providers.</summary>
[PublicAPI]
public sealed class FeaturesStorageOptions
{
    /// <summary>Gets or sets the database schema that contains the features tables. Default: <c>"features"</c>.</summary>
    public string Schema { get; set; } = "features";

    /// <summary>Gets or sets the name of the table that stores per-provider feature values. Default: <c>"FeatureValues"</c>.</summary>
    public string FeatureValuesTableName { get; set; } = "FeatureValues";

    /// <summary>Gets or sets the name of the table that stores feature definitions. Default: <c>"FeatureDefinitions"</c>.</summary>
    public string FeatureDefinitionsTableName { get; set; } = "FeatureDefinitions";

    /// <summary>Gets or sets the name of the table that stores feature group definitions. Default: <c>"FeatureGroupDefinitions"</c>.</summary>
    public string FeatureGroupDefinitionsTableName { get; set; } = "FeatureGroupDefinitions";

    /// <summary>
    /// When <see langword="true"/> (default), the startup storage initializer creates the schema, tables, and indexes
    /// on first run. Set to <see langword="false"/> when the schema is provisioned out-of-band (e.g., by a DBA or
    /// a migrations job); the initializer becomes a no-op but still signals completion so callers that await
    /// <c>WaitForInitializationAsync</c> do not block. Applies only to raw-DDL self-initializing providers
    /// (PostgreSQL, SQL Server); EF Core storage is always schema-managed by migrations.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

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
