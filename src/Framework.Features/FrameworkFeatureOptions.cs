// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Definitions;
using Framework.Features.Values;
using Framework.Kernel.Primitives;

namespace Framework.Features;

public class FrameworkFeatureOptions
{
    public TypeList<IFeatureDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<IFeatureValueProvider> ValueProviders { get; } = [];
}
