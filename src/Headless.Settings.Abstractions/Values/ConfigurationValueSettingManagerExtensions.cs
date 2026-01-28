using Headless.Settings.Models;

namespace Headless.Settings.Values;

[PublicAPI]
public static class ConfigurationValueSettingManagerExtensions
{
    extension(ISettingManager settingManager)
    {
        public async Task<bool> IsTrueInConfigurationAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager.IsTrueAsync(
                name,
                SettingValueProviderNames.Configuration,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public async Task<bool> IsFalseInConfigurationAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager.IsFalseAsync(
                name,
                SettingValueProviderNames.Configuration,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task<T?> FindInConfigurationAsync<T>(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.FindAsync<T>(
                name,
                SettingValueProviderNames.Configuration,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task<string?> FindInConfigurationAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.FindAsync(
                name,
                SettingValueProviderNames.Configuration,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task<List<SettingValue>> GetAllInConfigurationAsync(
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.GetAllAsync(
                SettingValueProviderNames.Configuration,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }
    }
}
