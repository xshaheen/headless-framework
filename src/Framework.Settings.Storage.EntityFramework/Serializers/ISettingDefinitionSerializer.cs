using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.BuildingBlocks.Helpers.System;
using Framework.Kernel.Primitives;
using Framework.Settings.Entities;
using Framework.Settings.Models;

namespace Framework.Settings.Serializers;

public interface ISettingDefinitionSerializer
{
    Task<SettingDefinitionRecord> SerializeAsync(SettingDefinition setting);

    Task<List<SettingDefinitionRecord>> SerializeAsync(IEnumerable<SettingDefinition> settings);
}

public sealed class SettingDefinitionSerializer(IGuidGenerator guidGenerator) : ISettingDefinitionSerializer
{
    public Task<SettingDefinitionRecord> SerializeAsync(SettingDefinition setting)
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

            return Task.FromResult(record);
        }
    }

    public async Task<List<SettingDefinitionRecord>> SerializeAsync(IEnumerable<SettingDefinition> settings)
    {
        var records = new List<SettingDefinitionRecord>();
        foreach (var setting in settings)
        {
            records.Add(await SerializeAsync(setting));
        }

        return records;
    }

    private static string? _SerializeProviders(ICollection<string> providers)
    {
        return providers.Count != 0 ? providers.JoinAsString(",") : null;
    }
}
