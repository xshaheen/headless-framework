using Framework.Settings.Entities;
using Framework.Settings.Models;

namespace Framework.Settings.DefinitionsStorage;

public sealed class DynamicSettingDefinitionStoreInMemoryCache
{
    private readonly Dictionary<string, SettingDefinition> _settingDefinitions = new(StringComparer.Ordinal);

    public string? CacheStamp { get; set; }

    public SemaphoreSlim SyncSemaphore { get; } = new(1, 1);

    public DateTime? LastCheckTime { get; set; }

    public Task FillAsync(List<SettingDefinitionRecord> settingRecords)
    {
        _settingDefinitions.Clear();

        foreach (var record in settingRecords)
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

            _settingDefinitions[record.Name] = settingDefinition;
        }

        return Task.CompletedTask;
    }

    public SettingDefinition? GetSettingOrDefault(string name)
    {
        return _settingDefinitions.GetOrDefault(name);
    }

    public IReadOnlyList<SettingDefinition> GetSettings()
    {
        return _settingDefinitions.Values.ToList();
    }
}
