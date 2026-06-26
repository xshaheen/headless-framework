// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;
using Headless.Primitives;
using Headless.Settings.Entities;
using Headless.Settings.Models;

namespace Headless.Settings.Definitions;

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

/// <summary>Default implementation of <see cref="ISettingDefinitionSerializer"/>.</summary>
public sealed class SettingDefinitionSerializer(IGuidGenerator guidGenerator) : ISettingDefinitionSerializer
{
    /// <inheritdoc/>
    public SettingDefinitionRecord Serialize(SettingDefinition setting)
    {
        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            var record = new SettingDefinitionRecord(
                id: guidGenerator.Create(),
                name: setting.Name,
                displayName: setting.DisplayName,
                description: setting.Description,
                defaultValue: setting.DefaultValue,
                providers: _SerializeProviders(setting.Providers),
                isVisibleToClients: setting.IsVisibleToClients,
                isInherited: setting.IsInherited,
                isEncrypted: setting.IsEncrypted
            );

            foreach (var property in setting.ExtraProperties)
            {
                record.SetProperty(property.Key, property.Value);
            }

            return record;
        }
    }

    /// <inheritdoc/>
    public List<SettingDefinitionRecord> Serialize(IEnumerable<SettingDefinition> settings)
    {
        return settings.Select(Serialize).ToList();
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
        return records.Select(Deserialize).ToList();
    }

    private static string? _SerializeProviders(List<string> providers)
    {
        return providers.Count != 0 ? providers.JoinAsString(",") : null;
    }
}
