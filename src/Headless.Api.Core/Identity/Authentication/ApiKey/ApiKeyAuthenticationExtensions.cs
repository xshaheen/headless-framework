// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Identity.Authentication.ApiKey;

/// <summary>Extension methods for registering the API key authentication scheme.</summary>
[PublicAPI]
public static class ApiKeyAuthenticationExtensions
{
    /// <summary>
    /// Registers the <see cref="ApiKeyAuthenticationHandler{TUser,TUserId}"/> and its backing
    /// <typeparamref name="TApiKeyStore"/> with the DI container.
    /// </summary>
    /// <typeparam name="TUser">The user type, derived from <see cref="IdentityUser{TKey}"/>.</typeparam>
    /// <typeparam name="TUserId">The type of the user's primary key.</typeparam>
    /// <typeparam name="TApiKeyStore">
    /// The <see cref="IApiKeyStore{TUser,TUserId}"/> implementation to register as transient.
    /// </typeparam>
    /// <param name="builder">The <see cref="AuthenticationBuilder"/> to add the scheme to.</param>
    /// <param name="authenticationScheme">
    /// Scheme name. Defaults to <see cref="ApiKeyAuthenticationSchemeOptions.DefaultScheme"/>.
    /// </param>
    /// <param name="displayName">
    /// Human-readable scheme name. Defaults to <see cref="ApiKeyAuthenticationSchemeOptions.DefaultScheme"/>.
    /// </param>
    /// <param name="configureOptions">Optional delegate to configure <see cref="ApiKeyAuthenticationSchemeOptions"/>.</param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
    public static AuthenticationBuilder AddApiKey<TUser, TUserId, TApiKeyStore>(
        this AuthenticationBuilder builder,
        string? authenticationScheme = null,
        string? displayName = null,
        Action<ApiKeyAuthenticationSchemeOptions>? configureOptions = null
    )
        where TUser : IdentityUser<TUserId>
        where TUserId : IEquatable<TUserId>
        where TApiKeyStore : class, IApiKeyStore<TUser, TUserId>
    {
        builder.Services.AddTransient<IApiKeyStore<TUser, TUserId>, TApiKeyStore>();

        return builder.AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler<TUser, TUserId>>(
            authenticationScheme ?? ApiKeyAuthenticationSchemeOptions.DefaultScheme,
            displayName ?? ApiKeyAuthenticationSchemeOptions.DefaultScheme,
            configureOptions
        );
    }
}
