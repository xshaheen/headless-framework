using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.BuildingBlocks.Helpers.System;
using Framework.Kernel.Primitives;
using Framework.Settings.Entities;
using Framework.Settings.Models;

namespace Framework.Settings.Definitions;

public interface ISettingDefinitionSerializer
{
    SettingDefinitionRecord Serialize(SettingDefinition setting);

    List<SettingDefinitionRecord> Serialize(IEnumerable<SettingDefinition> settings);
}

public sealed class SettingDefinitionSerializer(IGuidGenerator guidGenerator) : ISettingDefinitionSerializer
{
    public SettingDefinitionRecord Serialize(SettingDefinition setting)
    {
        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            var record = new SettingDefinitionRecord(
                guidGenerator.Create(),
                setting.Name,
                setting.DisplayName,
                setting.Description,
                setting.DefaultValue,
                _SerializeProviders(setting.Providers),
                setting.IsVisibleToClients,
                setting.IsInherited,
                setting.IsEncrypted
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

    private static string? _SerializeProviders(ICollection<string> providers)
    {
        return providers.Count != 0 ? providers.JoinAsString(",") : null;
    }
}
