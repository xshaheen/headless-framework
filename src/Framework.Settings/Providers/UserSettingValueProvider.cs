using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Settings.Definitions;
using Framework.Settings.Values;

namespace Framework.Settings.Providers;

/// <summary>Current user setting value provider.</summary>
public sealed class UserSettingValueProvider(ISettingStore settingStore, ICurrentUser currentUser)
    : SettingValueProvider(settingStore)
{
    public const string ProviderName = "User";

    public override string Name => ProviderName;

    public override async Task<string?> GetOrDefaultAsync(SettingDefinition setting)
    {
        return currentUser.UserId is null
            ? null
            : await SettingStore.GetOrDefaultAsync(setting.Name, Name, currentUser.UserId);
    }

    public override async Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings)
    {
        return currentUser.UserId is null
            ? settings.Select(x => new SettingValue(x.Name, value: null)).ToList()
            : await SettingStore.GetAllAsync(settings.Select(x => x.Name).ToArray(), Name, currentUser.UserId);
    }
}
