// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features;

[PublicAPI]
public sealed class FeaturesStorageOptions
{
    public string Schema { get; set; } = "features";

    public string FeatureValuesTableName { get; set; } = "FeatureValues";

    public string FeatureDefinitionsTableName { get; set; } = "FeatureDefinitions";

    public string FeatureGroupDefinitionsTableName { get; set; } = "FeatureGroupDefinitions";

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
    internal void CopyTo(FeaturesStorageOptions target)
    {
        target.Schema = Schema;
        target.FeatureValuesTableName = FeatureValuesTableName;
        target.FeatureDefinitionsTableName = FeatureDefinitionsTableName;
        target.FeatureGroupDefinitionsTableName = FeatureGroupDefinitionsTableName;
        target.InitializeOnStartup = InitializeOnStartup;
    }
}
