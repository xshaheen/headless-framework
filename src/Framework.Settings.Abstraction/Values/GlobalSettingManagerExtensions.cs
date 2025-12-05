using Framework.Settings.Models;

namespace Framework.Settings.Values;

[PublicAPI]
public static class GlobalSettingManagerExtensions
{
    extension(ISettingManager settingManager)
    {
        public async Task<bool> IsTrueGlobalAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager.IsTrueAsync(
                name,
                SettingValueProviderNames.Global,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public async Task<bool> IsFalseGlobalAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager.IsFalseAsync(
                name,
                SettingValueProviderNames.Global,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task<T?> FindGlobalAsync<T>(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.FindAsync<T>(
                name,
                SettingValueProviderNames.Global,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task<string?> FindGlobalAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.FindAsync(
                name,
                SettingValueProviderNames.Global,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task<List<SettingValue>> GetAllGlobalAsync(
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.GetAllAsync(
                SettingValueProviderNames.Global,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task SetGlobalAsync(string name, string? value, CancellationToken cancellationToken = default)
        {
            return settingManager.SetAsync(
                name,
                value,
                SettingValueProviderNames.Global,
                providerKey: null,
                cancellationToken: cancellationToken
            );
        }

        public Task SetGlobalAsync<T>(string name, T? value, CancellationToken cancellationToken = default)
        {
            return settingManager.SetAsync(
                name,
                value,
                SettingValueProviderNames.Global,
                providerKey: null,
                cancellationToken: cancellationToken
            );
        }
    }
}
