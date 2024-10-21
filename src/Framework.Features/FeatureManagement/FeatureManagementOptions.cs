// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Primitives;

namespace Framework.Features.FeatureManagement;

public class FeatureManagementOptions
{
    public TypeList<IFeatureManagementProvider> Providers { get; } = [];

    public Dictionary<string, string> ProviderPolicies { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Default: true.
    /// </summary>
    public bool SaveStaticFeaturesToDatabase { get; set; } = true;

    /// <summary>
    /// Default: false.
    /// </summary>
    public bool IsDynamicFeatureStoreEnabled { get; set; }
}
