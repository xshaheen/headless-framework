// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Primitives;
using Framework.Settings.Definitions;
using Framework.Settings.ValueProviders;

namespace Framework.Settings.Models;

public sealed class SettingManagementOptions
{
    public TypeList<ISettingDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<ISettingValueReadProvider> ValueProviders { get; } = [];

    public HashSet<string> DeletedSettings { get; } = [];

    /// <summary>Default: false.</summary>
    public bool IsDynamicSettingStoreEnabled { get; set; }

    /// <summary>Default: true.</summary>
    public bool SaveStaticSettingsToDatabase { get; set; } = true;
}
