// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Collections;
using Headless.Features.Definitions;
using Headless.Features.ValueProviders;

namespace Headless.Features.Models;

public sealed class FeatureManagementProvidersOptions
{
    public TypeList<IFeatureDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<IFeatureValueReadProvider> ValueProviders { get; } = [];

    public HashSet<string> DeletedFeatures { get; } = [];

    public HashSet<string> DeletedFeatureGroups { get; } = [];
}
