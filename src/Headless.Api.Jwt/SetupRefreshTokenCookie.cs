// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Security;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>
/// Extension members on <see cref="IServiceCollection"/> that register <see cref="IRefreshTokenCookie"/>, the
/// transport for a refresh token kept in an <c>HttpOnly</c> cookie.
/// </summary>
/// <remarks>
/// The registration adds the HTTP context accessor and, when none is registered, <see cref="TimeProvider.System"/>.
/// It registers no refresh-token store or endpoint.
/// </remarks>
[PublicAPI]
public static class SetupRefreshTokenCookie
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="IRefreshTokenCookie"/> and binds <see cref="RefreshTokenCookieOptions"/> from
        /// <paramref name="configuration"/>.
        /// </summary>
        /// <param name="configuration">The section bound to <see cref="RefreshTokenCookieOptions"/>.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup when <see cref="RefreshTokenCookieOptions"/> fails validation.
        /// </exception>
        public IServiceCollection AddHeadlessRefreshTokenCookie(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);
            services.Configure<RefreshTokenCookieOptions, RefreshTokenCookieOptionsValidator>(configuration);

            return services._AddRefreshTokenCookieCore();
        }

        /// <summary>
        /// Registers <see cref="IRefreshTokenCookie"/> and configures <see cref="RefreshTokenCookieOptions"/> with
        /// <paramref name="setupAction"/>.
        /// </summary>
        /// <param name="setupAction">Configures <see cref="RefreshTokenCookieOptions"/>.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup when <see cref="RefreshTokenCookieOptions"/> fails validation.
        /// </exception>
        public IServiceCollection AddHeadlessRefreshTokenCookie(Action<RefreshTokenCookieOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);
            services.Configure<RefreshTokenCookieOptions, RefreshTokenCookieOptionsValidator>(setupAction);

            return services._AddRefreshTokenCookieCore();
        }

        /// <summary>
        /// Registers <see cref="IRefreshTokenCookie"/> and configures <see cref="RefreshTokenCookieOptions"/> with
        /// <paramref name="setupAction"/>, which receives the application's <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="setupAction">Configures <see cref="RefreshTokenCookieOptions"/>.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup when <see cref="RefreshTokenCookieOptions"/> fails validation.
        /// </exception>
        public IServiceCollection AddHeadlessRefreshTokenCookie(
            Action<RefreshTokenCookieOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);
            services.Configure<RefreshTokenCookieOptions, RefreshTokenCookieOptionsValidator>(setupAction);

            return services._AddRefreshTokenCookieCore();
        }

        private IServiceCollection _AddRefreshTokenCookieCore()
        {
            services.AddHttpContextAccessor();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IRefreshTokenCookie, RefreshTokenCookie>();

            return services;
        }
    }
}
