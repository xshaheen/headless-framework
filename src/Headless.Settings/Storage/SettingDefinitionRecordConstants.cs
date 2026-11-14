// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings;

/// <summary>
/// Default column lengths for <see cref="SettingDefinitionRecord"/>. <see cref="SettingsStorageOptions"/> starts from them and is
/// what the schema, the EF Core model, and the repositories read, so configure a length there.
/// </summary>
public static class SettingDefinitionRecordConstants
{
    /// <summary>Maximum character length for <see cref="SettingDefinitionRecord.Name"/>.</summary>
    public const int NameMaxLength = 128;

    /// <summary>Maximum character length for <see cref="SettingDefinitionRecord.DisplayName"/>.</summary>
    public const int DisplayNameMaxLength = 256;

    /// <summary>Maximum character length for <see cref="SettingDefinitionRecord.Description"/>.</summary>
    public const int DescriptionMaxLength = 512;

    /// <summary>Maximum character length for <see cref="SettingDefinitionRecord.DefaultValue"/>. Mirrors <see cref="SettingValueRecordConstants.ValueMaxLength"/>.</summary>
    public const int DefaultValueMaxLength = SettingValueRecordConstants.ValueMaxLength;

    /// <summary>Maximum character length for the comma-separated <see cref="SettingDefinitionRecord.Providers"/> column.</summary>
    public const int ProvidersMaxLength = 1024;
}
