// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features;

/// <summary>
/// Default column lengths for <see cref="FeatureDefinitionRecord"/>. <see cref="FeaturesStorageOptions"/> starts from them and is
/// what the schema, the EF Core model, and the repositories read, so configure a length there.
/// </summary>
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
