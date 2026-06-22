// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Headless.Api.Identity.Authentication.Basic;

/// <summary>Extension methods for registering the Basic authentication scheme.</summary>
[PublicAPI]
public static class AuthenticationBuilderExtensions
{
    /// <summary>
    /// Registers the <see cref="BasicAuthenticationHandler{TUser,TUserId}"/> with the DI container.
    /// </summary>
    /// <typeparam name="TUser">The user type, derived from <see cref="IdentityUser{TKey}"/>.</typeparam>
    /// <typeparam name="TUserId">The type of the user's primary key.</typeparam>
    /// <param name="builder">The <see cref="AuthenticationBuilder"/> to add the scheme to.</param>
    /// <param name="authenticationScheme">
    /// Scheme name. Defaults to <see cref="BasicAuthenticationOptions.DefaultScheme"/>.
    /// </param>
    /// <param name="displayName">
    /// Human-readable scheme name. Defaults to <see cref="BasicAuthenticationOptions.DefaultScheme"/>.
    /// </param>
    /// <param name="configureOptions">Optional delegate to configure <see cref="BasicAuthenticationOptions"/>.</param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
    public static AuthenticationBuilder AddBasicSchema<TUser, TUserId>(
        this AuthenticationBuilder builder,
        string? authenticationScheme = null,
        string? displayName = null,
        Action<BasicAuthenticationOptions>? configureOptions = null
    )
        where TUser : IdentityUser<TUserId>
        where TUserId : IEquatable<TUserId>
    {
        return builder.AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler<TUser, TUserId>>(
            authenticationScheme ?? BasicAuthenticationOptions.DefaultScheme,
            displayName ?? BasicAuthenticationOptions.DefaultScheme,
            configureOptions ?? (_ => { })
        );
    }
}
