// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.Entities;

/// <summary>Column length limits for <see cref="SettingDefinitionRecord"/> fields.</summary>
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
