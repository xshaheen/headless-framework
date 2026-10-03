// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;
using Headless.Features.Entities;
using Headless.Features.Models;
using Headless.Primitives;

namespace Headless.Features.Definitions;

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
