using Framework.Settings.ValueProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Framework.Settings.ValueStores;

public interface ISettingStore
{
    Task<string?> GetOrNullAsync(string name, string? providerName, string? providerKey);

    Task<List<SettingValue>> GetAllAsync(string[] names, string? providerName, string? providerKey);
}

public sealed class NullSettingStore : ISettingStore
{
    public ILogger<NullSettingStore> Logger { get; set; } = NullLogger<NullSettingStore>.Instance;

    public Task<string?> GetOrNullAsync(string name, string? providerName, string? providerKey)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<List<SettingValue>> GetAllAsync(string[] names, string? providerName, string? providerKey)
    {
        var settingValues = names.Select(x => new SettingValue(x, null)).ToList();

        return Task.FromResult(settingValues);
    }
}
