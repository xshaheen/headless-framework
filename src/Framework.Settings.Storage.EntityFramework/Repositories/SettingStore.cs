using Framework.Settings.Values;

namespace Framework.Settings.Repositories;

public sealed class SettingStore(ISettingManagementStore store) : ISettingStore
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
