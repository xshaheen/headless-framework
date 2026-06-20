// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;

namespace Headless.Settings.Values;

/// <summary>General-purpose convenience extensions on <see cref="ISettingManager"/>.</summary>
[PublicAPI]
public static class SettingManagerExtensions
{
    extension(ISettingManager settingManager)
    {
        /// <summary>Returns <see langword="true"/> when the named setting value equals <c>"true"</c> (case-insensitive).</summary>
        /// <param name="settingName">The unique name of the setting.</param>
        /// <param name="providerName">Provider to query; <see langword="null"/> queries all providers in order.</param>
        /// <param name="providerKey">Provider-specific discriminator; <see langword="null"/> uses the provider's default.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the stored value is the string <c>"true"</c>; otherwise <see langword="false"/>.</returns>
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

        /// <summary>Returns <see langword="true"/> when the named setting value equals <c>"false"</c> (case-insensitive).</summary>
        /// <param name="settingName">The unique name of the setting.</param>
        /// <param name="providerName">Provider to query; <see langword="null"/> queries all providers in order.</param>
        /// <param name="providerKey">Provider-specific discriminator; <see langword="null"/> uses the provider's default.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the stored value is the string <c>"false"</c>; otherwise <see langword="false"/>.</returns>
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

        /// <summary>Finds a setting value and deserializes it to <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The target type to deserialize the stored JSON value into.</typeparam>
        /// <param name="settingName">The unique name of the setting.</param>
        /// <param name="providerName">Provider to query; <see langword="null"/> queries all providers in order.</param>
        /// <param name="providerKey">Provider-specific discriminator; <see langword="null"/> uses the provider's default.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when the setting has no value.</returns>
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

        /// <summary>Serializes <paramref name="value"/> to JSON and persists it through a specific provider.</summary>
        /// <typeparam name="T">The type of the value to serialize.</typeparam>
        /// <param name="settingName">The unique name of the setting to update.</param>
        /// <param name="value">The value to serialize and store, or <see langword="null"/> to store a JSON null.</param>
        /// <param name="providerName">The name of the provider that will store the value.</param>
        /// <param name="providerKey">Provider-specific discriminator; pass <see langword="null"/> for the provider's default key.</param>
        /// <param name="forceToSet">When <see langword="true"/>, bypasses provider-level read-only guards.</param>
        /// <param name="cancellationToken">The abort token.</param>
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
