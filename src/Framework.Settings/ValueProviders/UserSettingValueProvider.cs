using Framework.Api.Core.Abstractions;
using Framework.Settings.DefinitionProviders;
using Framework.Settings.ValueStores;

namespace Framework.Settings.ValueProviders;

public sealed class UserSettingValueProvider(ISettingStore settingStore, ICurrentUser currentUser)
    : SettingValueProvider(settingStore)
{
    public const string ProviderName = "User";

    public override string Name => ProviderName;

    public override async Task<string?> GetOrNullAsync(SettingDefinition setting)
    {
        return currentUser.UserId is null
            ? null
            : await SettingStore.GetOrNullAsync(setting.Name, Name, currentUser.UserId);
    }

    public override async Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings)
    {
        return currentUser.UserId is null
            ? settings.Select(x => new SettingValue(x.Name, null)).ToList()
            : await SettingStore.GetAllAsync(settings.Select(x => x.Name).ToArray(), Name, currentUser.UserId);
    }
}
