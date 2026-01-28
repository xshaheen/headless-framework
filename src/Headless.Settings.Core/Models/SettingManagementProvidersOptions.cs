// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Collections;
using Headless.Settings.Definitions;
using Headless.Settings.ValueProviders;

namespace Headless.Settings.Models;

public sealed class SettingManagementProvidersOptions
{
    public TypeList<ISettingDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<ISettingValueReadProvider> ValueProviders { get; } = [];

    public HashSet<string> DeletedSettings { get; } = [];
}
