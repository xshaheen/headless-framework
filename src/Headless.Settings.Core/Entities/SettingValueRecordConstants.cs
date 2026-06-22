// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.Entities;

/// <summary>Column length limits for <see cref="SettingValueRecord"/> fields.</summary>
public static class SettingValueRecordConstants
{
    /// <summary>Maximum character length for <see cref="SettingValueRecord.Name"/>.</summary>
    public const int NameMaxLength = 128;

    /// <summary>Maximum character length for <see cref="SettingValueRecord.Value"/>.</summary>
    public const int ValueMaxLength = 2000;

    /// <summary>Maximum character length for <see cref="SettingValueRecord.ProviderName"/>.</summary>
    public const int ProviderNameMaxLength = 64;

    /// <summary>Maximum character length for <see cref="SettingValueRecord.ProviderKey"/>.</summary>
    public const int ProviderKeyMaxLength = 64;
}
