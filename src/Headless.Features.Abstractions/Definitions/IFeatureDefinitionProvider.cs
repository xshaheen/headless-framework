// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;

namespace Headless.Features.Definitions;

/// <summary>Contributes feature definitions to the feature system during startup.</summary>
/// <remarks>
/// Implement this interface and register it with DI to declare the features your module provides.
/// The framework collects all registered providers and merges their definitions via <see cref="IFeatureDefinitionManager"/>.
/// </remarks>
public interface IFeatureDefinitionProvider
{
    /// <summary>Populates <paramref name="context"/> with feature groups and feature definitions for this provider.</summary>
    /// <param name="context">The mutable context used to add or modify feature groups and features.</param>
    void Define(IFeatureDefinitionContext context);
}
