// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Identity;

namespace Headless.Api.Identity.Authentication.ApiKey;

/// <summary>
/// Resolves a <typeparamref name="TUser"/> from a raw API key string.
/// Implement this interface to back the <see cref="ApiKeyAuthenticationHandler{TUser,TUserId}"/> with your own key store.
/// </summary>
/// <typeparam name="TUser">The user type, derived from <see cref="IdentityUser{TKey}"/>.</typeparam>
/// <typeparam name="TUserId">The type of the user's primary key.</typeparam>
public interface IApiKeyStore<TUser, TUserId>
    where TUser : IdentityUser<TUserId>
    where TUserId : IEquatable<TUserId>
{
    /// <summary>Looks up the user associated with <paramref name="apiKey"/> if the key exists and is active.</summary>
    /// <param name="apiKey">The raw API key value extracted from the request header or query string.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// The <typeparamref name="TUser"/> associated with the key when it exists and is active;
    /// <see langword="null"/> when the key is unknown or has been revoked/disabled.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<TUser?> GetActiveApiKeyUserAsync(string apiKey, CancellationToken cancellationToken = default);
}
