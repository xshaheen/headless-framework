using Framework.BuildingBlocks.Models.Collections;
using Framework.Settings.Definitions;
using Framework.Settings.Values;

namespace Framework.Settings;

public sealed class FrameworkSettingOptions
{
    public TypeList<ISettingDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<ISettingValueProvider> ValueProviders { get; } = [];
}
