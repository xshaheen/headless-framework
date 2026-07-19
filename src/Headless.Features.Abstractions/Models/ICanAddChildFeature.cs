// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.Models;

/// <summary>Implemented by types that can own child <see cref="FeatureDefinition"/> entries (groups and features).</summary>
public interface ICanAddChildFeature
{
    /// <summary>Creates and registers a child feature definition.</summary>
    /// <param name="options">The child feature name and optional metadata.</param>
    /// <returns>The newly created child <see cref="FeatureDefinition"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    FeatureDefinition AddChild(FeatureDefinitionCreateOptions options);
}
