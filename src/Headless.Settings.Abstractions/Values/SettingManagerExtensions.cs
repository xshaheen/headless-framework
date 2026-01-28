// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;

namespace Headless.Settings.Values;

[PublicAPI]
public static class SettingManagerExtensions
{
    extension(ISettingManager settingManager)
    {
        public async Task<bool> IsTrueAsync(
            string settingName,
            string? providerName = null,
            string? providerKey = null,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            var value = await settingManager.FindAsync(
                settingName,
                providerName,
                providerKey,
                fallback,
                cancellationToken
            );

            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<bool> IsFalseAsync(
            string settingName,
            string? providerName = null,
            string? providerKey = null,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            var value = await settingManager.FindAsync(
                settingName,
                providerName,
                providerKey,
                fallback,
                cancellationToken
            );

            return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<T?> FindAsync<T>(
            string settingName,
            string? providerName = null,
            string? providerKey = null,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            var value = await settingManager.FindAsync(
                settingName,
                providerName,
                providerKey,
                fallback,
                cancellationToken
            );

            return string.IsNullOrEmpty(value)
                ? default
                : JsonSerializer.Deserialize<T>(value, JsonConstants.DefaultInternalJsonOptions);
        }

        public Task SetAsync<T>(
            string settingName,
            T? value,
            string providerName,
            string? providerKey,
            bool forceToSet = false,
            CancellationToken cancellationToken = default
        )
        {
            var valueJson = JsonSerializer.Serialize(value, JsonConstants.DefaultInternalJsonOptions);

            return settingManager.SetAsync(
                settingName,
                valueJson,
                providerName,
                providerKey,
                forceToSet,
                cancellationToken
            );
        }
    }
}
