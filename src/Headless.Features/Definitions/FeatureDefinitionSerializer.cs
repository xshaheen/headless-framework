// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Checks;
using Headless.Primitives;
using Headless.Serializer;
using Microsoft.Extensions.Options;

namespace Headless.Features;

/// <summary>
/// Default <see cref="IFeatureDefinitionSerializer"/> implementation. Serializing refuses a value longer than the
/// column <see cref="FeaturesStorageOptions"/> configures for it, so an over-long definition fails here with the
/// offending value instead of at the database.
/// </summary>
public sealed class FeatureDefinitionSerializer(
    IGuidGenerator guidGenerator,
    IOptions<FeaturesStorageOptions> storageOptions
) : IFeatureDefinitionSerializer
{
    private readonly FeaturesStorageOptions _lengths = storageOptions.Value;

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
        _EnsureFits(group.Name, _lengths.FeatureGroupDefinitionsNameMaxLength);
        _EnsureFits(group.DisplayName, _lengths.FeatureGroupDefinitionsDisplayNameMaxLength);

        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            var record = new FeatureGroupDefinitionRecord(guidGenerator.Create(), group.Name, group.DisplayName);

            // The record is compared with the stored one to decide whether the store is current, so its bag holds what
            // the store reads back (a long for an int, a name for an enum), not the values the code declared.
            foreach (var property in JsonConstants.DefaultStorageJsonOptions.RoundTrip(group.ExtraProperties) ?? [])
            {
                record.SetProperty(property.Key, property.Value);
            }

            return record;
        }
    }

    /// <inheritdoc/>
    public FeatureDefinitionRecord Serialize(FeatureDefinition feature, FeatureGroupDefinition group)
    {
        var providers = _SerializeProviders(feature.Providers);
        _EnsureFits(group.Name, _lengths.FeatureGroupDefinitionsNameMaxLength);
        _EnsureFits(feature.Name, _lengths.FeatureDefinitionsNameMaxLength);
        _EnsureFits(feature.Parent?.Name, _lengths.FeatureDefinitionsNameMaxLength);
        _EnsureFits(feature.DisplayName, _lengths.FeatureDefinitionsDisplayNameMaxLength);
        _EnsureFits(feature.Description, _lengths.FeatureDefinitionsDescriptionMaxLength);
        _EnsureFits(feature.DefaultValue, _lengths.FeatureDefinitionsDefaultValueMaxLength);
        _EnsureFits(providers, _lengths.FeatureDefinitionsProvidersMaxLength);

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
                providers
            );

            // The record is compared with the stored one to decide whether the store is current, so its bag holds what
            // the store reads back (a long for an int, a name for an enum), not the values the code declared.
            foreach (var property in JsonConstants.DefaultStorageJsonOptions.RoundTrip(feature.ExtraProperties) ?? [])
            {
                featureRecord.SetProperty(property.Key, property.Value);
            }

            return featureRecord;
        }
    }

    /// <summary>Refuses a value longer than its column, naming the value; a null value has no length to check.</summary>
    private static void _EnsureFits(
        string? value,
        int maxLength,
        [CallerArgumentExpression(nameof(value))] string? name = null
    )
    {
        if (value is not null)
        {
            Argument.IsLessThanOrEqualTo(value.Length, maxLength, paramName: name);
        }
    }

    private static string? _SerializeProviders(List<string> providers)
    {
        return providers.Count != 0 ? providers.JoinAsString(',') : null;
    }
}

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
