// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Headless.Settings.Values;

/// <summary>Extensions on <see cref="ISettingManager"/> that scope queries to the <see cref="SettingValueProviderNames.DefaultValue"/> provider.</summary>
[PublicAPI]
public static class DefaultSettingManagerExtensions
{
    extension(ISettingManager settingManager)
    {
        /// <summary>Returns <see langword="true"/> when the setting's default value equals <c>"true"</c> (case-insensitive).</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no default value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the default value is the string <c>"true"</c>; otherwise <see langword="false"/>.</returns>
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

        /// <summary>Returns <see langword="true"/> when the setting's default value equals <c>"false"</c> (case-insensitive).</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no default value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the default value is the string <c>"false"</c>; otherwise <see langword="false"/>.</returns>
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

        /// <summary>Finds the default setting value and deserializes it to <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The target type to deserialize the stored JSON value into.</typeparam>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no default value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when no default value is present.</returns>
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

        /// <summary>Returns the raw string default value for the named setting.</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no default value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The default value string, or <see langword="null"/> if no default is set.</returns>
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

        /// <summary>Returns all setting values from the <see cref="SettingValueProviderNames.DefaultValue"/> provider.</summary>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers for settings without a default value.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>A list of <see cref="SettingValue"/> instances for all settings served by the default-value provider.</returns>
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
