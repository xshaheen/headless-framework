// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;
using Headless.Features.Entities;
using Headless.Features.Models;
using Headless.Primitives;

namespace Headless.Features.Definitions;

/// <summary>Converts in-memory feature definition models into their database record equivalents.</summary>
public interface IFeatureDefinitionSerializer
{
    /// <summary>
    /// Serializes all groups in <paramref name="groups"/> — including their nested features — into flat lists of
    /// <see cref="FeatureGroupDefinitionRecord"/> and <see cref="FeatureDefinitionRecord"/> suitable for persistence.
    /// </summary>
    /// <param name="groups">The feature group definitions to serialize.</param>
    /// <returns>
    /// A tuple of (<see cref="FeatureGroupDefinitionRecord"/> collection, <see cref="FeatureDefinitionRecord"/> collection).
    /// </returns>
    (IReadOnlyCollection<FeatureGroupDefinitionRecord>, IReadOnlyCollection<FeatureDefinitionRecord>) Serialize(
        IEnumerable<FeatureGroupDefinition> groups
    );

    /// <summary>Serializes a single <paramref name="group"/> into a <see cref="FeatureGroupDefinitionRecord"/>.</summary>
    /// <param name="group">The feature group definition to serialize.</param>
    /// <returns>A new <see cref="FeatureGroupDefinitionRecord"/> representing the group.</returns>
    FeatureGroupDefinitionRecord Serialize(FeatureGroupDefinition group);

    /// <summary>Serializes a single <paramref name="feature"/> into a <see cref="FeatureDefinitionRecord"/>.</summary>
    /// <param name="feature">The feature definition to serialize.</param>
    /// <param name="group">The group that owns <paramref name="feature"/>; its name is embedded in the record.</param>
    /// <returns>A new <see cref="FeatureDefinitionRecord"/> representing the feature.</returns>
    FeatureDefinitionRecord Serialize(FeatureDefinition feature, FeatureGroupDefinition group);
}
