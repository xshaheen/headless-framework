// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved


namespace Framework.Settings.Models;

public sealed class SettingManagementOptions
{
    /// <summary>Default: false.</summary>
    public bool IsDynamicSettingStoreEnabled { get; set; }

    /// <summary>Default: true.</summary>
    public bool SaveStaticSettingsToDatabase { get; set; } = true;
}
