// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.Entities;

/// <summary>Column-length limits for <see cref="FeatureDefinitionRecord"/> fields.</summary>
public static class FeatureDefinitionRecordConstants
{
    /// <summary>Maximum length (128) for <see cref="FeatureDefinitionRecord.Name"/> and <see cref="FeatureDefinitionRecord.GroupName"/>.</summary>
    public const int NameMaxLength = 128;

    /// <summary>Maximum length (256) for <see cref="FeatureDefinitionRecord.DisplayName"/>.</summary>
    public const int DisplayNameMaxLength = 256;

    /// <summary>Maximum length (256) for <see cref="FeatureDefinitionRecord.Description"/>.</summary>
    public const int DescriptionMaxLength = 256;

    /// <summary>Maximum length (256) for <see cref="FeatureDefinitionRecord.DefaultValue"/>.</summary>
    public const int DefaultValueMaxLength = 256;

    /// <summary>Maximum length (256) for the comma-separated <see cref="FeatureDefinitionRecord.Providers"/> column.</summary>
    public const int ProvidersMaxLength = 256;
}
