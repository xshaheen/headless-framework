// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Entities;
using Framework.Features.FeatureManagement;
using Framework.Features.Models;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.BuildingBlocks.Helpers.System;

namespace Framework.Features.Definitions;

public interface IFeatureDefinitionSerializer
{
    Task<(FeatureGroupDefinitionRecord[], FeatureDefinitionRecord[])> SerializeAsync(
        IEnumerable<FeatureGroupDefinition> featureGroups
    );

    Task<FeatureGroupDefinitionRecord> SerializeAsync(FeatureGroupDefinition featureGroup);

    Task<FeatureDefinitionRecord> SerializeAsync(FeatureDefinition feature, FeatureGroupDefinition? featureGroup);
}

public sealed class FeatureDefinitionSerializer(
    IGuidGenerator guidGenerator,
    StringValueTypeSerializer stringValueTypeSerializer
) : IFeatureDefinitionSerializer
{
    public async Task<(FeatureGroupDefinitionRecord[], FeatureDefinitionRecord[])> SerializeAsync(
        IEnumerable<FeatureGroupDefinition> featureGroups
    )
    {
        var featureGroupRecords = new List<FeatureGroupDefinitionRecord>();
        var featureRecords = new List<FeatureDefinitionRecord>();

        foreach (var featureGroup in featureGroups)
        {
            featureGroupRecords.Add(await SerializeAsync(featureGroup));

            foreach (var feature in featureGroup.GetFeaturesWithChildren())
            {
                featureRecords.Add(await SerializeAsync(feature, featureGroup));
            }
        }

        return (featureGroupRecords.ToArray(), featureRecords.ToArray());
    }

    public Task<FeatureGroupDefinitionRecord> SerializeAsync(FeatureGroupDefinition featureGroup)
    {
        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            var featureGroupRecord = new FeatureGroupDefinitionRecord(
                guidGenerator.Create(),
                featureGroup.Name,
                LocalizableStringSerializer.Serialize(featureGroup.DisplayName)
            );

            foreach (var property in featureGroup.Properties)
            {
                featureGroupRecord.SetProperty(property.Key, property.Value);
            }

            return Task.FromResult(featureGroupRecord);
        }
    }

    public Task<FeatureDefinitionRecord> SerializeAsync(FeatureDefinition feature, FeatureGroupDefinition featureGroup)
    {
        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            var featureRecord = new FeatureDefinitionRecord(
                guidGenerator.Create(),
                featureGroup?.Name,
                feature.Name,
                feature.Parent?.Name,
                LocalizableStringSerializer.Serialize(feature.DisplayName),
                LocalizableStringSerializer.Serialize(feature.Description),
                feature.DefaultValue,
                feature.IsVisibleToClients,
                feature.IsAvailableToHost,
                _SerializeProviders(feature.AllowedProviders),
                _SerializeStringValueType(feature.ValueType)
            );

            foreach (var property in feature.Properties)
            {
                featureRecord.SetProperty(property.Key, property.Value);
            }

            return Task.FromResult(featureRecord);
        }
    }

    private static string? _SerializeProviders(ICollection<string> providers)
    {
        return providers.Count != 0 ? providers.JoinAsString(",") : null;
    }

    private string _SerializeStringValueType(IStringValueType stringValueType)
    {
        return stringValueTypeSerializer.Serialize(stringValueType);
    }
}
