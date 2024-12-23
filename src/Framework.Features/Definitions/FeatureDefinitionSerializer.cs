// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Abstractions;
using Framework.Core;
using Framework.Features.Entities;
using Framework.Features.Models;
using Framework.Primitives;

namespace Framework.Features.Definitions;

public interface IFeatureDefinitionSerializer
{
    (IReadOnlyCollection<FeatureGroupDefinitionRecord>, IReadOnlyCollection<FeatureDefinitionRecord>) Serialize(
        IEnumerable<FeatureGroupDefinition> groups
    );

    FeatureGroupDefinitionRecord Serialize(FeatureGroupDefinition group);

    FeatureDefinitionRecord Serialize(FeatureDefinition feature, FeatureGroupDefinition group);
}

public sealed class FeatureDefinitionSerializer(IGuidGenerator guidGenerator) : IFeatureDefinitionSerializer
{
    public (IReadOnlyCollection<FeatureGroupDefinitionRecord>, IReadOnlyCollection<FeatureDefinitionRecord>) Serialize(
        IEnumerable<FeatureGroupDefinition> groups
    )
    {
        var featureGroupRecords = new List<FeatureGroupDefinitionRecord>();
        var featureRecords = new List<FeatureDefinitionRecord>();

        foreach (var featureGroup in groups)
        {
            featureGroupRecords.Add(Serialize(featureGroup));

            foreach (var feature in featureGroup.GetFlatFeatures())
            {
                featureRecords.Add(Serialize(feature, featureGroup));
            }
        }

        return (featureGroupRecords, featureRecords);
    }

    public FeatureGroupDefinitionRecord Serialize(FeatureGroupDefinition group)
    {
        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            var record = new FeatureGroupDefinitionRecord(guidGenerator.Create(), group.Name, group.DisplayName);

            foreach (var property in group.Properties)
            {
                record.SetProperty(property.Key, property.Value);
            }

            return record;
        }
    }

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

            foreach (var property in feature.Properties)
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
