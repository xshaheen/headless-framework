// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Collections;
using Framework.Settings.Definitions;
using Framework.Settings.ValueProviders;

namespace Framework.Settings.Models;

public sealed class SettingManagementProvidersOptions
{
    public TypeList<ISettingDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<ISettingValueReadProvider> ValueProviders { get; } = [];

    public HashSet<string> DeletedSettings { get; } = [];
}
