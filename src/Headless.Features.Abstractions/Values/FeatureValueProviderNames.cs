// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.Values;

/// <summary>Well-known provider name constants used by the feature value system.</summary>
[PublicAPI]
public static class FeatureValueProviderNames
{
    /// <summary>Provider name for tenant-scoped feature values.</summary>
    public const string Tenant = "Tenant";

    /// <summary>Provider name for edition-scoped feature values.</summary>
    public const string Edition = "Edition";

    /// <summary>Provider name for the static default values defined on each <see cref="Headless.Features.Models.FeatureDefinition"/>.</summary>
    public const string DefaultValue = "DefaultValue";
}
