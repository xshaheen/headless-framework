// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Collections;
using Headless.Features.Definitions;
using Headless.Features.ValueProviders;

namespace Headless.Features.Models;

/// <summary>
/// Registers the ordered provider type lists and deletion tombstone sets used by the feature management pipeline.
/// </summary>
/// <remarks>
/// Consumers do not typically interact with this class directly; use
/// <c>IServiceCollection.AddFeatureDefinitionProvider&lt;T&gt;</c> and
/// <c>IServiceCollection.AddFeatureValueProvider&lt;T&gt;</c> instead.
/// The <see cref="DeletedFeatures"/> and <see cref="DeletedFeatureGroups"/> sets let the startup
/// initializer purge dynamic records for features that have been removed from code.
/// </remarks>
public sealed class FeatureManagementProvidersOptions
{
    /// <summary>
    /// Ordered list of <see cref="IFeatureDefinitionProvider"/> implementation types invoked during static store
    /// initialization to populate the feature catalog.
    /// </summary>
    public TypeList<IFeatureDefinitionProvider> DefinitionProviders { get; } = [];

    /// <summary>
    /// Ordered list of <see cref="IFeatureValueReadProvider"/> implementation types consulted when resolving a
    /// feature's effective value for a given subject.
    /// </summary>
    public TypeList<IFeatureValueReadProvider> ValueProviders { get; } = [];

    /// <summary>
    /// Names of features that have been removed from the application and should be deleted from the dynamic store
    /// on the next save.
    /// </summary>
    public HashSet<string> DeletedFeatures { get; } = [];

    /// <summary>
    /// Names of feature groups that have been removed from the application and should be deleted from the dynamic
    /// store (along with all their features) on the next save.
    /// </summary>
    public HashSet<string> DeletedFeatureGroups { get; } = [];
}
