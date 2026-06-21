// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Headless.Settings.Values;

/// <summary>Extensions on <see cref="ISettingManager"/> that scope queries to the <see cref="SettingValueProviderNames.Configuration"/> provider.</summary>
[PublicAPI]
public static class ConfigurationValueSettingManagerExtensions
{
    extension(ISettingManager settingManager)
    {
        /// <summary>Returns <see langword="true"/> when the setting's configuration value equals <c>"true"</c> (case-insensitive).</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no configuration value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the configuration value is the string <c>"true"</c>; otherwise <see langword="false"/>.</returns>
        public async Task<bool> IsTrueInConfigurationAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager
                .IsTrueAsync(
                    name,
                    SettingValueProviderNames.Configuration,
                    providerKey: null,
                    fallback,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        /// <summary>Returns <see langword="true"/> when the setting's configuration value equals <c>"false"</c> (case-insensitive).</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no configuration value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the configuration value is the string <c>"false"</c>; otherwise <see langword="false"/>.</returns>
        public async Task<bool> IsFalseInConfigurationAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager
                .IsFalseAsync(
                    name,
                    SettingValueProviderNames.Configuration,
                    providerKey: null,
                    fallback,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        /// <summary>Finds the setting value from <c>IConfiguration</c> and deserializes it to <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The target type to deserialize the stored JSON value into.</typeparam>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no configuration value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when no configuration value is present.</returns>
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

        /// <summary>Returns the raw string value for the named setting from <c>IConfiguration</c>.</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no configuration value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The configuration value string, or <see langword="null"/> if no value is configured.</returns>
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

        /// <summary>Returns all setting values from the <see cref="SettingValueProviderNames.Configuration"/> provider.</summary>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers for settings without a configuration value.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>A list of <see cref="SettingValue"/> instances for all settings served by the configuration provider.</returns>
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
