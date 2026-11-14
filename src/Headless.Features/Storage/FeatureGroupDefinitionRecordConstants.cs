// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features;

/// <summary>
/// Default column lengths for <see cref="FeatureGroupDefinitionRecord"/>. <see cref="FeaturesStorageOptions"/> starts from them and is
/// what the schema, the EF Core model, and the repositories read, so configure a length there.
/// </summary>
public static class FeatureGroupDefinitionRecordConstants
{
    /// <summary>Maximum length (128) for <see cref="FeatureGroupDefinitionRecord.Name"/>.</summary>
    public const int NameMaxLength = 128;

    /// <summary>Maximum length (256) for <see cref="FeatureGroupDefinitionRecord.DisplayName"/>.</summary>
    public const int DisplayNameMaxLength = 256;
}
