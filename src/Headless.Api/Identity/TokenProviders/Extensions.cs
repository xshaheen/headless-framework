// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Identity.TokenProviders;

[PublicAPI]
public static class IdentityBuilderExtensions
{
    #region Password Reset

    public static IdentityBuilder AddPasswordResetTokenProvider<TUser>(
        this IdentityBuilder builder,
        Action<PasswordResetTokenProviderOptions>? configureOptions
    )
        where TUser : class
    {
        var options = new PasswordResetTokenProviderOptions();

        if (configureOptions is not null)
        {
            configureOptions(options);
            builder.Services.Configure(configureOptions);
        }

        builder.Services.Configure<IdentityOptions>(identityOptions =>
        {
            identityOptions.Tokens.PasswordResetTokenProvider = options.Name;
        });

        builder.AddTokenProvider<PasswordResetTokenProvider<TUser>>(options.Name);

        return builder;
    }

    public static IdentityBuilder AddPasswordResetCodeProvider<TUser>(
        this IdentityBuilder builder,
        Action<PasswordResetCodeProviderOptions>? configureOptions
    )
        where TUser : class
    {
        var options = new PasswordResetCodeProviderOptions();

        if (configureOptions is not null)
        {
            configureOptions(options);
            builder.Services.Configure(configureOptions);
        }

        builder.Services.Configure<IdentityOptions>(identityOptions =>
        {
            identityOptions.Tokens.PasswordResetTokenProvider = options.Name;
        });

        builder.AddTokenProvider<PasswordResetCodeProvider<TUser>>(options.Name);

        return builder;
    }

    #endregion

    #region Email Confirmation

    public static IdentityBuilder AddEmailConfirmationTokenProvider<TUser>(
        this IdentityBuilder builder,
        Action<EmailConfirmationTokenProviderOptions>? configureOptions
    )
        where TUser : class
    {
        var options = new EmailConfirmationTokenProviderOptions();

        if (configureOptions is not null)
        {
            configureOptions(options);
            builder.Services.Configure(configureOptions);
        }

        builder.Services.Configure<IdentityOptions>(identityOptions =>
        {
            identityOptions.Tokens.EmailConfirmationTokenProvider = options.Name;
        });

        builder.AddTokenProvider<EmailConfirmationTokenProvider<TUser>>(options.Name);

        return builder;
    }

    public static IdentityBuilder AddEmailConfirmationCodeProvider<TUser>(
        this IdentityBuilder builder,
        Action<EmailConfirmationCodeProviderOptions>? configureOptions
    )
        where TUser : class
    {
        var options = new EmailConfirmationCodeProviderOptions();

        if (configureOptions is not null)
        {
            configureOptions(options);
            builder.Services.Configure(configureOptions);
        }

        builder.Services.Configure<IdentityOptions>(identityOptions =>
        {
            identityOptions.Tokens.EmailConfirmationTokenProvider = options.Name;
        });

        builder.AddTokenProvider<EmailConfirmationCodeProvider<TUser>>(options.Name);

        return builder;
    }

    #endregion
}
