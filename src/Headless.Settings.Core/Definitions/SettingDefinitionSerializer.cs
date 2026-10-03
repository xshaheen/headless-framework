// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;
using Headless.Primitives;
using Headless.Settings.Entities;
using Headless.Settings.Models;

namespace Headless.Settings.Definitions;

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

    private static string? _SerializeProviders(List<string> providers)
    {
        return providers.Count != 0 ? providers.JoinAsString(",") : null;
    }
}
