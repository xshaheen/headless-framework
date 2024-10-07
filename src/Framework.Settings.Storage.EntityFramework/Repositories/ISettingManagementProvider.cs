using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Settings.Definitions;
using Framework.Settings.Models;
using Framework.Settings.ValueProviders;
using Microsoft.Extensions.Configuration;

namespace Framework.Settings.Repositories;

public interface ISettingManagementProvider
{
    string Name { get; }

    Task<string?> GetOrNullAsync(SettingDefinition setting, string? providerKey);

    Task SetAsync(SettingDefinition setting, string value, string? providerKey);

    Task ClearAsync(SettingDefinition setting, string? providerKey);
}

public abstract class SettingManagementProvider(ISettingManagementStore store) : ISettingManagementProvider
{
    public abstract string Name { get; }

    protected ISettingManagementStore Store { get; } = store;

    public virtual async Task<string?> GetOrNullAsync(SettingDefinition setting, string? providerKey)
    {
        return await Store.GetOrNullAsync(setting.Name, Name, NormalizeProviderKey(providerKey));
    }

    public virtual async Task SetAsync(SettingDefinition setting, string value, string? providerKey)
    {
        await Store.SetAsync(setting.Name, value, Name, NormalizeProviderKey(providerKey));
    }

    public virtual async Task ClearAsync(SettingDefinition setting, string? providerKey)
    {
        await Store.DeleteAsync(setting.Name, Name, NormalizeProviderKey(providerKey));
    }

    protected virtual string? NormalizeProviderKey(string? providerKey)
    {
        return providerKey;
    }
}

public sealed class DefaultValueSettingManagementProvider : ISettingManagementProvider
{
    public string Name => DefaultValueSettingValueProvider.ProviderName;

    public Task<string?> GetOrNullAsync(SettingDefinition setting, string? providerKey)
    {
        return Task.FromResult(setting.DefaultValue);
    }

    public Task SetAsync(SettingDefinition setting, string value, string? providerKey)
    {
        throw new InvalidOperationException(
            $"Can not set default value of a setting. It is only possible while defining the setting in a {typeof(ISettingDefinitionProvider)} implementation."
        );
    }

    public Task ClearAsync(SettingDefinition setting, string? providerKey)
    {
        throw new InvalidOperationException(
            $"Can not clear default value of a setting. It is only possible while defining the setting in a {typeof(ISettingDefinitionProvider)} implementation."
        );
    }
}

public sealed class ConfigurationSettingManagementProvider(IConfiguration configuration) : ISettingManagementProvider
{
    public string Name => ConfigurationSettingValueProvider.ProviderName;

    public Task<string?> GetOrNullAsync(SettingDefinition setting, string? providerKey)
    {
        return Task.FromResult(configuration[ConfigurationSettingValueProvider.ConfigurationNamePrefix + setting.Name]);
    }

    public Task SetAsync(SettingDefinition setting, string value, string? providerKey)
    {
        throw new InvalidOperationException("Can not set a setting value to the application configuration.");
    }

    public Task ClearAsync(SettingDefinition setting, string? providerKey)
    {
        throw new InvalidOperationException("Can not set a setting value to the application configuration.");
    }
}

public sealed class GlobalSettingManagementProvider(ISettingManagementStore store) : SettingManagementProvider(store)
{
    public override string Name => GlobalSettingValueProvider.ProviderName;

    protected override string? NormalizeProviderKey(string? providerKey) => null;
}

public sealed class TenantSettingManagementProvider(ISettingManagementStore store, ICurrentTenant currentTenant)
    : SettingManagementProvider(store)
{
    public override string Name => TenantSettingValueProvider.ProviderName;

    protected override string? NormalizeProviderKey(string? providerKey)
    {
        return providerKey ?? currentTenant.Id?.ToString();
    }
}

public sealed class UserSettingManagementProvider(ISettingManagementStore store, ICurrentUser currentUser)
    : SettingManagementProvider(store)
{
    public override string Name => UserSettingValueProvider.ProviderName;

    protected override string? NormalizeProviderKey(string? providerKey)
    {
        if (providerKey is not null)
        {
            return providerKey;
        }

        return currentUser.UserId?.ToString();
    }
}
