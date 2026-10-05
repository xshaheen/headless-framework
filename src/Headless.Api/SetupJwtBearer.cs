// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Security;
using Headless.Checks;
using Headless.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>
/// Extension members on <see cref="AuthenticationBuilder"/> that register a JWT bearer scheme validating the tokens
/// <see cref="IJwtTokenFactory"/> issues.
/// </summary>
/// <remarks>
/// The scheme validates with the same rules as <see cref="IJwtTokenFactory.ParseJwtTokenAsync"/>: HMAC-SHA256
/// signature from the UTF-8 signing key, optional decryption key, required expiry, zero clock skew, the Headless
/// name and role claim types, and no inbound claim mapping. Configure the keys and expected claims once in
/// <see cref="HeadlessJwtBearerOptions"/> instead of rebuilding <c>TokenValidationParameters</c> by hand.
/// </remarks>
[PublicAPI]
public static class SetupJwtBearer
{
    extension(AuthenticationBuilder builder)
    {
        /// <summary>
        /// Adds a JWT bearer scheme and binds <see cref="HeadlessJwtBearerOptions"/> from <paramref name="configuration"/>.
        /// </summary>
        /// <param name="configuration">The section bound to <see cref="HeadlessJwtBearerOptions"/>.</param>
        /// <param name="authenticationScheme">The scheme name. Defaults to <c>Bearer</c>.</param>
        /// <param name="configureBearer">
        /// Optional callback that runs after the Headless validation rules, for events such as reading a token from
        /// the query string. Changing <c>TokenValidationParameters</c> here makes the scheme diverge from the factory.
        /// </param>
        /// <returns>The same <see cref="AuthenticationBuilder"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup when <see cref="HeadlessJwtBearerOptions"/> fails validation.
        /// </exception>
        public AuthenticationBuilder AddHeadlessJwtBearer(
            IConfiguration configuration,
            string authenticationScheme = AuthenticationConstants.Schemas.Bearer,
            Action<JwtBearerOptions>? configureBearer = null
        )
        {
            Argument.IsNotNull(configuration);
            Argument.IsNotNullOrWhiteSpace(authenticationScheme);

            builder.Services.Configure<HeadlessJwtBearerOptions, HeadlessJwtBearerOptionsValidator>(
                configuration,
                authenticationScheme
            );

            return builder._AddHeadlessJwtBearerCore(authenticationScheme, configureBearer);
        }

        /// <summary>
        /// Adds a JWT bearer scheme and configures <see cref="HeadlessJwtBearerOptions"/> with <paramref name="setupAction"/>.
        /// </summary>
        /// <param name="setupAction">Configures <see cref="HeadlessJwtBearerOptions"/>.</param>
        /// <param name="authenticationScheme">The scheme name. Defaults to <c>Bearer</c>.</param>
        /// <param name="configureBearer">
        /// Optional callback that runs after the Headless validation rules, for events such as reading a token from
        /// the query string. Changing <c>TokenValidationParameters</c> here makes the scheme diverge from the factory.
        /// </param>
        /// <returns>The same <see cref="AuthenticationBuilder"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup when <see cref="HeadlessJwtBearerOptions"/> fails validation.
        /// </exception>
        public AuthenticationBuilder AddHeadlessJwtBearer(
            Action<HeadlessJwtBearerOptions> setupAction,
            string authenticationScheme = AuthenticationConstants.Schemas.Bearer,
            Action<JwtBearerOptions>? configureBearer = null
        )
        {
            Argument.IsNotNull(setupAction);
            Argument.IsNotNullOrWhiteSpace(authenticationScheme);

            builder.Services.Configure<HeadlessJwtBearerOptions, HeadlessJwtBearerOptionsValidator>(
                setupAction,
                authenticationScheme
            );

            return builder._AddHeadlessJwtBearerCore(authenticationScheme, configureBearer);
        }

        /// <summary>
        /// Adds a JWT bearer scheme and configures <see cref="HeadlessJwtBearerOptions"/> with <paramref name="setupAction"/>,
        /// which receives the application's <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="setupAction">Configures <see cref="HeadlessJwtBearerOptions"/>.</param>
        /// <param name="authenticationScheme">The scheme name. Defaults to <c>Bearer</c>.</param>
        /// <param name="configureBearer">
        /// Optional callback that runs after the Headless validation rules, for events such as reading a token from
        /// the query string. Changing <c>TokenValidationParameters</c> here makes the scheme diverge from the factory.
        /// </param>
        /// <returns>The same <see cref="AuthenticationBuilder"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup when <see cref="HeadlessJwtBearerOptions"/> fails validation.
        /// </exception>
        public AuthenticationBuilder AddHeadlessJwtBearer(
            Action<HeadlessJwtBearerOptions, IServiceProvider> setupAction,
            string authenticationScheme = AuthenticationConstants.Schemas.Bearer,
            Action<JwtBearerOptions>? configureBearer = null
        )
        {
            Argument.IsNotNull(setupAction);
            Argument.IsNotNullOrWhiteSpace(authenticationScheme);

            builder.Services.Configure<HeadlessJwtBearerOptions, HeadlessJwtBearerOptionsValidator>(
                setupAction,
                authenticationScheme
            );

            return builder._AddHeadlessJwtBearerCore(authenticationScheme, configureBearer);
        }

        private AuthenticationBuilder _AddHeadlessJwtBearerCore(
            string authenticationScheme,
            Action<JwtBearerOptions>? configureBearer
        )
        {
            // Registered before AddJwtBearer so the consumer's configureBearer callback runs after these rules.
            builder
                .Services.AddOptions<JwtBearerOptions>(authenticationScheme)
                .Configure<IOptionsMonitor<HeadlessJwtBearerOptions>>(
                    (bearer, monitor) =>
                    {
                        var options = monitor.Get(authenticationScheme);

                        // The factory's handler keeps the issued claim types; mapping them would rename "sub",
                        // "role", and the Headless claims before authorization reads them.
                        bearer.MapInboundClaims = false;
                        bearer.TokenValidationParameters = JwtTokenFactory.CreateTokenValidationParameters(
                            options.SigningKey,
                            options.EncryptingKey,
                            options.Issuer,
                            options.Audience,
                            options.ValidateIssuer,
                            options.ValidateAudience
                        );
                    }
                );

            return builder.AddJwtBearer(authenticationScheme, configureBearer ?? (static _ => { }));
        }
    }
}
