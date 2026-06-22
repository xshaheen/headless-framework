// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.Entities;

/// <summary>Column-length limits for <see cref="FeatureValueRecord"/> fields.</summary>
public static class FeatureValueRecordConstants
{
    /// <summary>Maximum length (128) for <see cref="FeatureValueRecord.Name"/>.</summary>
    public const int NameMaxLength = 128;

    /// <summary>Maximum length (64) for <see cref="FeatureValueRecord.ProviderName"/>.</summary>
    public const int ProviderNameMaxLength = 64;

    /// <summary>Maximum length (64) for <see cref="FeatureValueRecord.ProviderKey"/>.</summary>
    public const int ProviderKeyMaxLength = 64;

    /// <summary>Maximum length (128) for <see cref="FeatureValueRecord.Value"/>.</summary>
    public const int ValueMaxLength = 128;
}
