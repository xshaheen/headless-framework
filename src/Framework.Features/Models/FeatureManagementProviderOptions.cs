// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Definitions;
using Framework.Features.ValueProviders;
using Framework.Features.Values;
using Framework.Kernel.Primitives;

namespace Framework.Features.Models;

public sealed class FeatureManagementProviderOptions
{
    public TypeList<IFeatureDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<IFeatureValueReadProvider> ValueProviders { get; } = [];
}
