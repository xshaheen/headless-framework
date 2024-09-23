// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Primitives;
using Framework.Settings.Definitions;
using Framework.Settings.Values;

namespace Framework.Settings;

public sealed class FrameworkSettingOptions
{
    public TypeList<ISettingDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<ISettingValueProvider> ValueProviders { get; } = [];

    public HashSet<string> DeletedSettings { get; } = [];
}
