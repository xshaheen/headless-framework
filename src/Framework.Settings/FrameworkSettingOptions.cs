using Framework.BuildingBlocks.Models.Collections;
using Framework.Settings.DefinitionProviders;
using Framework.Settings.ValueStores;

namespace Framework.Settings;

public sealed class FrameworkSettingOptions
{
    public TypeList<ISettingDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<ISettingValueProvider> ValueProviders { get; } = [];
}
