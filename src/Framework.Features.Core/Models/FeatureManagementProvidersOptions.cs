// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Collections;
using Framework.Features.Definitions;
using Framework.Features.ValueProviders;

namespace Framework.Features.Models;

public sealed class FeatureManagementProvidersOptions
{
    public TypeList<IFeatureDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<IFeatureValueReadProvider> ValueProviders { get; } = [];

    public HashSet<string> DeletedFeatures { get; } = [];

    public HashSet<string> DeletedFeatureGroups { get; } = [];
}
