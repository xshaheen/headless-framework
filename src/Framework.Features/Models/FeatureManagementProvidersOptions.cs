// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Definitions;
using Framework.Features.ValueProviders;
using Framework.Primitives;

namespace Framework.Features.Models;

public sealed class FeatureManagementProvidersOptions
{
    public TypeList<IFeatureDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<IFeatureValueReadProvider> ValueProviders { get; } = [];

    public HashSet<string> DeletedFeatures { get; } = [];

    public HashSet<string> DeletedFeatureGroups { get; } = [];
}
