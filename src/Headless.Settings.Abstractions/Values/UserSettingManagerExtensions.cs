using Headless.Settings.Models;

namespace Headless.Settings.Values;

[PublicAPI]
public static class UserSettingManagerExtensions
{
    extension(ISettingManager settingManager)
    {
        public async Task<bool> IsTrueForUserAsync(
            string userId,
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager.IsTrueAsync(
                name,
                SettingValueProviderNames.User,
                userId,
                fallback,
                cancellationToken
            );
        }

        public async Task<bool> IsTrueForCurrentUserAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager.IsTrueAsync(
                name,
                SettingValueProviderNames.User,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public async Task<bool> IsFalseForUserAsync(
            string userId,
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager.IsFalseAsync(
                name,
                SettingValueProviderNames.User,
                userId,
                fallback,
                cancellationToken
            );
        }

        public async Task<bool> IsFalseForCurrentUserAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager.IsFalseAsync(
                name,
                SettingValueProviderNames.User,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task<T?> FindForUserAsync<T>(
            string userId,
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.FindAsync<T>(
                name,
                SettingValueProviderNames.User,
                userId,
                fallback,
                cancellationToken
            );
        }

        public Task<T?> FindForCurrentUserAsync<T>(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.FindAsync<T>(
                name,
                SettingValueProviderNames.User,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task<string?> FindForUserAsync(
            string userId,
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.FindAsync(name, SettingValueProviderNames.User, userId, fallback, cancellationToken);
        }

        public Task<string?> FindForCurrentUserAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.FindAsync(
                name,
                SettingValueProviderNames.User,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task<List<SettingValue>> GetAllForUserAsync(
            string userId,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.GetAllAsync(SettingValueProviderNames.User, userId, fallback, cancellationToken);
        }

        public Task<List<SettingValue>> GetAllForCurrentUserAsync(
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.GetAllAsync(
                SettingValueProviderNames.User,
                providerKey: null,
                fallback,
                cancellationToken
            );
        }

        public Task SetForUserAsync(
            string userId,
            string name,
            string? value,
            bool forceToSet = false,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.SetAsync(
                name,
                value,
                SettingValueProviderNames.User,
                userId,
                forceToSet,
                cancellationToken
            );
        }

        public Task SetForCurrentUserAsync(
            string name,
            string? value,
            bool forceToSet = false,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.SetAsync(
                name,
                value,
                SettingValueProviderNames.User,
                providerKey: null,
                forceToSet,
                cancellationToken
            );
        }

        public Task SetForUserAsync<T>(
            string userId,
            string name,
            T? value,
            bool forceToSet = false,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.SetAsync(
                name,
                value,
                SettingValueProviderNames.User,
                userId,
                forceToSet,
                cancellationToken
            );
        }

        public Task SetForCurrentUserAsync<T>(
            string name,
            T? value,
            bool forceToSet = false,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.SetAsync(
                name,
                value,
                SettingValueProviderNames.User,
                providerKey: null,
                forceToSet,
                cancellationToken
            );
        }
    }
}
