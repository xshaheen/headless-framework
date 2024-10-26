// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Features.Models;

public sealed class FeatureManagementOptions
{
    public Dictionary<string, string> ProviderPolicies { get; } = new(StringComparer.Ordinal);

    /// <summary>Default: true.</summary>
    public bool SaveStaticFeaturesToDatabase { get; set; } = true;

    /// <summary>Default: false.</summary>
    public bool IsDynamicFeatureStoreEnabled { get; set; }
}
