// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.Entities;

/// <summary>Column-length limits for <see cref="FeatureGroupDefinitionRecord"/> fields.</summary>
public static class FeatureGroupDefinitionRecordConstants
{
    /// <summary>Maximum length (128) for <see cref="FeatureGroupDefinitionRecord.Name"/>.</summary>
    public const int NameMaxLength = 128;

    /// <summary>Maximum length (256) for <see cref="FeatureGroupDefinitionRecord.DisplayName"/>.</summary>
    public const int DisplayNameMaxLength = 256;
}
