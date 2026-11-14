// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Checks;
using Headless.Primitives;
using Headless.Serializer;
using Microsoft.Extensions.Options;

namespace Headless.Settings;

/// <summary>
/// Default implementation of <see cref="ISettingDefinitionSerializer"/>. Serializing refuses a value longer than the
/// column <see cref="SettingsStorageOptions"/> configures for it, so an over-long definition fails here with the
/// offending value instead of at the database.
/// </summary>
public sealed class SettingDefinitionSerializer(
    IGuidGenerator guidGenerator,
    IOptions<SettingsStorageOptions> storageOptions
) : ISettingDefinitionSerializer
{
    private readonly SettingsStorageOptions _lengths = storageOptions.Value;

    /// <inheritdoc/>
    public SettingDefinitionRecord Serialize(SettingDefinition setting)
    {
        var providers = _SerializeProviders(setting.Providers);
        _EnsureFits(setting.Name, _lengths.SettingDefinitionsNameMaxLength);
        _EnsureFits(setting.DisplayName, _lengths.SettingDefinitionsDisplayNameMaxLength);
        _EnsureFits(setting.Description, _lengths.SettingDefinitionsDescriptionMaxLength);
        _EnsureFits(setting.DefaultValue, _lengths.SettingDefinitionsDefaultValueMaxLength);
        _EnsureFits(providers, _lengths.SettingDefinitionsProvidersMaxLength);

        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            var record = new SettingDefinitionRecord(
                id: guidGenerator.Create(),
                name: setting.Name,
                displayName: setting.DisplayName,
                description: setting.Description,
                defaultValue: setting.DefaultValue,
                providers: providers,
                isVisibleToClients: setting.IsVisibleToClients,
                isInherited: setting.IsInherited,
                isEncrypted: setting.IsEncrypted
            );

            // The record is compared with the stored one to decide whether the store is current, so its bag holds what
            // the store reads back (a long for an int, a name for an enum), not the values the code declared.
            foreach (var property in JsonConstants.DefaultStorageJsonOptions.RoundTrip(setting.ExtraProperties) ?? [])
            {
                record.SetProperty(property.Key, property.Value);
            }

            return record;
        }
    }

    /// <inheritdoc/>
    public List<SettingDefinitionRecord> Serialize(IEnumerable<SettingDefinition> settings)
    {
        return [.. settings.Select(Serialize)];
    }

    /// <inheritdoc/>
    public SettingDefinition Deserialize(SettingDefinitionRecord record)
    {
        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            var settingDefinition = new SettingDefinition(
                record.Name,
                record.DefaultValue,
                record.DisplayName,
                record.Description,
                record.IsVisibleToClients,
                record.IsInherited,
                record.IsEncrypted
            );

            if (!record.Providers.IsNullOrWhiteSpace())
            {
                settingDefinition.Providers.AddRange(
                    record.Providers.Split(',', StringSplitOptions.RemoveEmptyEntries)
                );
            }

            foreach (var property in record.ExtraProperties)
            {
                settingDefinition[property.Key] = property.Value;
            }

            return settingDefinition;
        }
    }

    /// <inheritdoc/>
    public List<SettingDefinition> Deserialize(IEnumerable<SettingDefinitionRecord> records)
    {
        return [.. records.Select(Deserialize)];
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
        return providers.Count != 0 ? providers.JoinAsString(",") : null;
    }
}

/// <summary>Converts between <see cref="SettingDefinition"/> domain objects and <see cref="SettingDefinitionRecord"/> persistence entities.</summary>
public interface ISettingDefinitionSerializer
{
    /// <summary>Converts a single <see cref="SettingDefinition"/> to its corresponding <see cref="SettingDefinitionRecord"/>.</summary>
    /// <param name="setting">The setting definition to serialize.</param>
    /// <returns>A new <see cref="SettingDefinitionRecord"/> representing <paramref name="setting"/>.</returns>
    SettingDefinitionRecord Serialize(SettingDefinition setting);

    /// <summary>Converts a collection of <see cref="SettingDefinition"/> objects to their corresponding <see cref="SettingDefinitionRecord"/> list.</summary>
    /// <param name="settings">The setting definitions to serialize.</param>
    /// <returns>A list of <see cref="SettingDefinitionRecord"/> instances, one per input definition.</returns>
    List<SettingDefinitionRecord> Serialize(IEnumerable<SettingDefinition> settings);

    /// <summary>Reconstructs a <see cref="SettingDefinition"/> from its persisted <see cref="SettingDefinitionRecord"/>.</summary>
    /// <param name="record">The record to deserialize.</param>
    /// <returns>The <see cref="SettingDefinition"/> represented by <paramref name="record"/>.</returns>
    SettingDefinition Deserialize(SettingDefinitionRecord record);

    /// <summary>Reconstructs a list of <see cref="SettingDefinition"/> objects from their persisted <see cref="SettingDefinitionRecord"/> instances.</summary>
    /// <param name="records">The records to deserialize.</param>
    /// <returns>A list of <see cref="SettingDefinition"/> instances, one per input record.</returns>
    List<SettingDefinition> Deserialize(IEnumerable<SettingDefinitionRecord> records);
}
