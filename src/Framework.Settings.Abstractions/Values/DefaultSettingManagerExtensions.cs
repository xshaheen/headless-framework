using Framework.Settings.Models;

namespace Framework.Settings.Values;

[PublicAPI]
public static class DefaultSettingManagerExtensions
{
    extension(ISettingManager settingManager)
    {
        public async Task<bool> IsTrueDefaultAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager.IsTrueAsync(
                name,
                SettingValueProviderNames.DefaultValue,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public async Task<bool> IsFalseDefaultAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager.IsFalseAsync(
                name,
                SettingValueProviderNames.DefaultValue,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task<T?> FindDefaultAsync<T>(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.FindAsync<T>(
                name,
                SettingValueProviderNames.DefaultValue,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task<string?> FindDefaultAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.FindAsync(
                name,
                SettingValueProviderNames.DefaultValue,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task<List<SettingValue>> GetAllDefaultAsync(
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.GetAllAsync(
                SettingValueProviderNames.DefaultValue,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }
    }
}
