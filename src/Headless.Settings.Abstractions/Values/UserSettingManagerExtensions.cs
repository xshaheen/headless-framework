// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Headless.Settings.Values;

/// <summary>Extensions on <see cref="ISettingManager"/> that scope queries to the <see cref="SettingValueProviderNames.User"/> provider.</summary>
[PublicAPI]
public static class UserSettingManagerExtensions
{
    extension(ISettingManager settingManager)
    {
        /// <summary>Returns <see langword="true"/> when the named setting value for the specified user equals <c>"true"</c> (case-insensitive).</summary>
        /// <param name="userId">The identifier of the user whose setting is queried.</param>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no user value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the user's value is the string <c>"true"</c>; otherwise <see langword="false"/>.</returns>
        public async Task<bool> IsTrueForUserAsync(
            string userId,
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager
                .IsTrueAsync(name, SettingValueProviderNames.User, userId, fallback, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Returns <see langword="true"/> when the named setting value for the ambient current user equals <c>"true"</c> (case-insensitive).</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no value is found for the current user.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the current user's value is the string <c>"true"</c>; otherwise <see langword="false"/>.</returns>
        public async Task<bool> IsTrueForCurrentUserAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager
                .IsTrueAsync(name, SettingValueProviderNames.User, providerKey: null, fallback, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Returns <see langword="true"/> when the named setting value for the specified user equals <c>"false"</c> (case-insensitive).</summary>
        /// <param name="userId">The identifier of the user whose setting is queried.</param>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no user value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the user's value is the string <c>"false"</c>; otherwise <see langword="false"/>.</returns>
        public async Task<bool> IsFalseForUserAsync(
            string userId,
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager
                .IsFalseAsync(name, SettingValueProviderNames.User, userId, fallback, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Returns <see langword="true"/> when the named setting value for the ambient current user equals <c>"false"</c> (case-insensitive).</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no value is found for the current user.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the current user's value is the string <c>"false"</c>; otherwise <see langword="false"/>.</returns>
        public async Task<bool> IsFalseForCurrentUserAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager
                .IsFalseAsync(name, SettingValueProviderNames.User, providerKey: null, fallback, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Finds the setting value for the specified user and deserializes it to <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The target type to deserialize the stored JSON value into.</typeparam>
        /// <param name="userId">The identifier of the user whose setting is queried.</param>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no user value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when no value is found for the user.</returns>
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

        /// <summary>Finds the setting value for the ambient current user and deserializes it to <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The target type to deserialize the stored JSON value into.</typeparam>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no value is found for the current user.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when no value is found for the current user.</returns>
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

        /// <summary>Returns the raw string setting value for the specified user.</summary>
        /// <param name="userId">The identifier of the user whose setting is queried.</param>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no user value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The setting value string, or <see langword="null"/> if no value is set for the user.</returns>
        public Task<string?> FindForUserAsync(
            string userId,
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.FindAsync(name, SettingValueProviderNames.User, userId, fallback, cancellationToken);
        }

        /// <summary>Returns the raw string setting value for the ambient current user.</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no value is found for the current user.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The setting value string, or <see langword="null"/> if no value is set for the current user.</returns>
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

        /// <summary>Returns all setting values from the <see cref="SettingValueProviderNames.User"/> provider scoped to the specified user.</summary>
        /// <param name="userId">The identifier of the user whose settings are retrieved.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers for settings without a user value.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>A list of <see cref="SettingValue"/> instances for the specified user.</returns>
        public Task<List<SettingValue>> GetAllForUserAsync(
            string userId,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.GetAllAsync(SettingValueProviderNames.User, userId, fallback, cancellationToken);
        }

        /// <summary>Returns all setting values from the <see cref="SettingValueProviderNames.User"/> provider scoped to the ambient current user.</summary>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers for settings without a value for the current user.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>A list of <see cref="SettingValue"/> instances for the current user.</returns>
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

        /// <summary>Persists a string setting value scoped to the specified user.</summary>
        /// <param name="userId">The identifier of the user for which the setting is stored.</param>
        /// <param name="name">The unique name of the setting to update.</param>
        /// <param name="value">The value to store, or <see langword="null"/> to clear it.</param>
        /// <param name="forceToSet">When <see langword="true"/>, bypasses provider-level read-only guards.</param>
        /// <param name="cancellationToken">The abort token.</param>
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

        /// <summary>Persists a string setting value scoped to the ambient current user.</summary>
        /// <param name="name">The unique name of the setting to update.</param>
        /// <param name="value">The value to store, or <see langword="null"/> to clear it.</param>
        /// <param name="forceToSet">When <see langword="true"/>, bypasses provider-level read-only guards.</param>
        /// <param name="cancellationToken">The abort token.</param>
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

        /// <summary>Serializes <paramref name="value"/> to JSON and persists it scoped to the specified user.</summary>
        /// <typeparam name="T">The type of the value to serialize.</typeparam>
        /// <param name="userId">The identifier of the user for which the setting is stored.</param>
        /// <param name="name">The unique name of the setting to update.</param>
        /// <param name="value">The value to serialize and store, or <see langword="null"/> to store a JSON null.</param>
        /// <param name="forceToSet">When <see langword="true"/>, bypasses provider-level read-only guards.</param>
        /// <param name="cancellationToken">The abort token.</param>
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

        /// <summary>Serializes <paramref name="value"/> to JSON and persists it scoped to the ambient current user.</summary>
        /// <typeparam name="T">The type of the value to serialize.</typeparam>
        /// <param name="name">The unique name of the setting to update.</param>
        /// <param name="value">The value to serialize and store, or <see langword="null"/> to store a JSON null.</param>
        /// <param name="forceToSet">When <see langword="true"/>, bypasses provider-level read-only guards.</param>
        /// <param name="cancellationToken">The abort token.</param>
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
