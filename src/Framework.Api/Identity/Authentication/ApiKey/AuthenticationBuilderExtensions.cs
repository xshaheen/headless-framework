// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Identity.Authentication.ApiKey;

[PublicAPI]
public static class ApiKeyAuthenticationExtensions
{
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
