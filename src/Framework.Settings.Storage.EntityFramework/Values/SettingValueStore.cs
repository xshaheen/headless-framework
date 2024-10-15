using Framework.Settings.Models;

namespace Framework.Settings.Values;

public sealed class SettingValueStore(ISettingManagementStore store) : ISettingValueStore
{
    public Task<string?> GetOrDefaultAsync(string name, string? providerName, string? providerKey)
    {
        return store.GetOrNullAsync(name, providerName, providerKey);
    }

    public Task<List<SettingValue>> GetAllAsync(string[] names, string? providerName, string? providerKey)
    {
        return store.GetListAsync(names, providerName, providerKey);
    }
}
