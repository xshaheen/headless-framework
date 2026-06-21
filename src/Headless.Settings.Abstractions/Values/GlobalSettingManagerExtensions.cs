// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Headless.Settings.Values;

/// <summary>Extensions on <see cref="ISettingManager"/> that scope queries to the <see cref="SettingValueProviderNames.Global"/> provider.</summary>
[PublicAPI]
public static class GlobalSettingManagerExtensions
{
    extension(ISettingManager settingManager)
    {
        /// <summary>Returns <see langword="true"/> when the global setting value equals <c>"true"</c> (case-insensitive).</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no global value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the global value is the string <c>"true"</c>; otherwise <see langword="false"/>.</returns>
        public async Task<bool> IsTrueGlobalAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager
                .IsTrueAsync(name, SettingValueProviderNames.Global, providerKey: null, fallback, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Returns <see langword="true"/> when the global setting value equals <c>"false"</c> (case-insensitive).</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no global value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the global value is the string <c>"false"</c>; otherwise <see langword="false"/>.</returns>
        public async Task<bool> IsFalseGlobalAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager
                .IsFalseAsync(name, SettingValueProviderNames.Global, providerKey: null, fallback, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Finds the global setting value and deserializes it to <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The target type to deserialize the stored JSON value into.</typeparam>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no global value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when no global value is present.</returns>
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

        /// <summary>Returns the raw string global value for the named setting.</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no global value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The global value string, or <see langword="null"/> if no global value is set.</returns>
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

        /// <summary>Returns all setting values from the <see cref="SettingValueProviderNames.Global"/> provider.</summary>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers for settings without a global value.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>A list of <see cref="SettingValue"/> instances for all settings served by the global provider.</returns>
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

        /// <summary>Persists a string setting value at the global scope.</summary>
        /// <param name="name">The unique name of the setting to update.</param>
        /// <param name="value">The value to store, or <see langword="null"/> to clear it.</param>
        /// <param name="cancellationToken">The abort token.</param>
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

        /// <summary>Serializes <paramref name="value"/> to JSON and persists it at the global scope.</summary>
        /// <typeparam name="T">The type of the value to serialize.</typeparam>
        /// <param name="name">The unique name of the setting to update.</param>
        /// <param name="value">The value to serialize and store, or <see langword="null"/> to store a JSON null.</param>
        /// <param name="cancellationToken">The abort token.</param>
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
