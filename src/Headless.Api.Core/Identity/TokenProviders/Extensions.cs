// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Api.Identity.TokenProviders;

/// <summary>Extension methods for registering Headless token providers with ASP.NET Core Identity.</summary>
[PublicAPI]
public static class IdentityBuilderExtensions
{
    #region Password Reset

    /// <summary>
    /// Registers <see cref="PasswordResetTokenProvider{TUser}"/> (data-protection-based, opaque link token)
    /// and sets it as the default <see cref="Microsoft.AspNetCore.Identity.TokenOptions.PasswordResetTokenProvider"/>.
    /// </summary>
    /// <typeparam name="TUser">The user type managed by ASP.NET Core Identity.</typeparam>
    /// <param name="builder">The <see cref="IdentityBuilder"/> to configure.</param>
    /// <param name="configureOptions">Optional delegate to override <see cref="PasswordResetTokenProviderOptions"/>.</param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
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
            identityOptions.Tokens.PasswordResetTokenProvider = options.Name
        );

        builder.AddTokenProvider<PasswordResetTokenProvider<TUser>>(options.Name);

        return builder;
    }

    /// <summary>
    /// Registers <see cref="PasswordResetCodeProvider{TUser}"/> (TOTP-based, 6-digit code)
    /// and sets it as the default <see cref="Microsoft.AspNetCore.Identity.TokenOptions.PasswordResetTokenProvider"/>.
    /// </summary>
    /// <typeparam name="TUser">The user type managed by ASP.NET Core Identity.</typeparam>
    /// <param name="builder">The <see cref="IdentityBuilder"/> to configure.</param>
    /// <param name="configureOptions">Optional delegate to override <see cref="PasswordResetCodeProviderOptions"/>.</param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
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

        builder.Services.TryAddSingleton<TotpRfc6238Generator>();

        builder.Services.Configure<IdentityOptions>(identityOptions =>
            identityOptions.Tokens.PasswordResetTokenProvider = options.Name
        );

        builder.AddTokenProvider<PasswordResetCodeProvider<TUser>>(options.Name);

        return builder;
    }

    #endregion

    #region Email Confirmation

    /// <summary>
    /// Registers <see cref="EmailConfirmationTokenProvider{TUser}"/> (data-protection-based, opaque link token)
    /// and sets it as the default <see cref="Microsoft.AspNetCore.Identity.TokenOptions.EmailConfirmationTokenProvider"/>.
    /// </summary>
    /// <typeparam name="TUser">The user type managed by ASP.NET Core Identity.</typeparam>
    /// <param name="builder">The <see cref="IdentityBuilder"/> to configure.</param>
    /// <param name="configureOptions">Optional delegate to override <see cref="EmailConfirmationTokenProviderOptions"/>.</param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
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
            identityOptions.Tokens.EmailConfirmationTokenProvider = options.Name
        );

        builder.AddTokenProvider<EmailConfirmationTokenProvider<TUser>>(options.Name);

        return builder;
    }

    /// <summary>
    /// Registers <see cref="EmailConfirmationCodeProvider{TUser}"/> (TOTP-based, 6-digit code)
    /// and sets it as the default <see cref="Microsoft.AspNetCore.Identity.TokenOptions.EmailConfirmationTokenProvider"/>.
    /// </summary>
    /// <typeparam name="TUser">The user type managed by ASP.NET Core Identity.</typeparam>
    /// <param name="builder">The <see cref="IdentityBuilder"/> to configure.</param>
    /// <param name="configureOptions">Optional delegate to override <see cref="EmailConfirmationCodeProviderOptions"/>.</param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
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

        builder.Services.TryAddSingleton<TotpRfc6238Generator>();

        builder.Services.Configure<IdentityOptions>(identityOptions =>
            identityOptions.Tokens.EmailConfirmationTokenProvider = options.Name
        );

        builder.AddTokenProvider<EmailConfirmationCodeProvider<TUser>>(options.Name);

        return builder;
    }

    #endregion
}
