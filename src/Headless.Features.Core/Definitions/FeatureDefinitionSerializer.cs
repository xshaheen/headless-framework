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

/// <summary>Default <see cref="IFeatureDefinitionSerializer"/> implementation.</summary>
public sealed class FeatureDefinitionSerializer(IGuidGenerator guidGenerator) : IFeatureDefinitionSerializer
{
    /// <inheritdoc/>
    public (IReadOnlyCollection<FeatureGroupDefinitionRecord>, IReadOnlyCollection<FeatureDefinitionRecord>) Serialize(
        IEnumerable<FeatureGroupDefinition> groups
    )
    {
        var featureGroupRecords = new List<FeatureGroupDefinitionRecord>();
        var featureRecords = new List<FeatureDefinitionRecord>();

        foreach (var featureGroup in groups)
        {
            featureGroupRecords.Add(Serialize(featureGroup));
            featureRecords.AddRange(featureGroup.GetFlatFeatures().Select(feature => Serialize(feature, featureGroup)));
        }

        return (featureGroupRecords, featureRecords);
    }

    /// <inheritdoc/>
    public FeatureGroupDefinitionRecord Serialize(FeatureGroupDefinition group)
    {
        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            var record = new FeatureGroupDefinitionRecord(guidGenerator.Create(), group.Name, group.DisplayName);

            foreach (var property in group.ExtraProperties)
            {
                record.SetProperty(property.Key, property.Value);
            }

            return record;
        }
    }

    /// <inheritdoc/>
    public FeatureDefinitionRecord Serialize(FeatureDefinition feature, FeatureGroupDefinition group)
    {
        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            var featureRecord = new FeatureDefinitionRecord(
                guidGenerator.Create(),
                group.Name,
                feature.Name,
                feature.Parent?.Name,
                feature.DisplayName,
                feature.Description,
                feature.DefaultValue,
                feature.IsVisibleToClients,
                feature.IsAvailableToHost,
                _SerializeProviders(feature.Providers)
            );

            foreach (var property in feature.ExtraProperties)
            {
                featureRecord.SetProperty(property.Key, property.Value);
            }

            return featureRecord;
        }
    }

    private static string? _SerializeProviders(List<string> providers)
    {
        return providers.Count != 0 ? providers.JoinAsString(',') : null;
    }
}
