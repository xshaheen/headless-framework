// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Framework.Api.Identity.Authentication.Basic;

[PublicAPI]
public static class AuthenticationBuilderExtensions
{
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
