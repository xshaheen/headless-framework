using Framework.Api.Core.Abstractions;
using Framework.Settings.DefinitionProviders;
using Framework.Settings.ValueStores;

namespace Framework.Settings.ValueProviders;

public sealed class TenantSettingValueProvider(ISettingStore settingStore, ICurrentTenant currentTenant)
    : SettingValueProvider(settingStore)
{
    public const string ProviderName = "Tenant";

    public override string Name => ProviderName;

    public override Task<string?> GetOrDefaultAsync(SettingDefinition setting)
    {
        return SettingStore.GetOrDefaultAsync(setting.Name, Name, currentTenant.Id?.ToString());
    }

    public override Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings)
    {
        return SettingStore.GetAllAsync(
            names: settings.Select(x => x.Name).ToArray(),
            providerName: Name,
            providerKey: currentTenant.Id?.ToString()
        );
    }
}
