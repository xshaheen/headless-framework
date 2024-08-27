using Framework.BuildingBlocks.Models.Collections;
using Framework.Features.Definitions;
using Framework.Features.Values;

namespace Framework.Features;

public class FrameworkFeatureOptions
{
    public TypeList<IFeatureDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<IFeatureValueProvider> ValueProviders { get; } = [];
}
