// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Abstractions;
using Framework.BuildingBlocks.Helpers.System;
using Framework.Primitives;
using Framework.Settings.Entities;
using Framework.Settings.Models;

namespace Framework.Settings.Definitions;

public interface ISettingDefinitionSerializer
{
    SettingDefinitionRecord Serialize(SettingDefinition setting);

    List<SettingDefinitionRecord> Serialize(IEnumerable<SettingDefinition> settings);

    SettingDefinition Deserialize(SettingDefinitionRecord record);

    List<SettingDefinition> Deserialize(IEnumerable<SettingDefinitionRecord> records);
}

public sealed class SettingDefinitionSerializer(IGuidGenerator guidGenerator) : ISettingDefinitionSerializer
{
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

            foreach (var property in setting.Properties)
            {
                record.SetProperty(property.Key, property.Value);
            }

            return record;
        }
    }

    public List<SettingDefinitionRecord> Serialize(IEnumerable<SettingDefinition> settings)
    {
        return settings.Select(Serialize).ToList();
    }

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

    public List<SettingDefinition> Deserialize(IEnumerable<SettingDefinitionRecord> records)
    {
        return records.Select(Deserialize).ToList();
    }

    private static string? _SerializeProviders(ICollection<string> providers)
    {
        return providers.Count != 0 ? providers.JoinAsString(",") : null;
    }
}
