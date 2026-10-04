// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Provides extension methods for registering dashboard authentication services.
/// </summary>
[PublicAPI]
public static class SetupDashboardAuthentication
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Binds <see cref="AuthConfig"/> from <paramref name="configuration"/> with startup validation
        /// and registers the scoped <see cref="IAuthService"/>.
        /// </summary>
        /// <param name="configuration">The configuration section to bind into <see cref="AuthConfig"/>.</param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddDashboardAuthentication(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            services.Configure<AuthConfig, AuthConfigValidator>(configuration);

            return _AddDashboardAuthenticationCore(services);
        }

        /// <summary>
        /// Configures <see cref="AuthConfig"/> with startup validation and registers the scoped <see cref="IAuthService"/>.
        /// </summary>
        /// <param name="setupAction">The delegate that configures <see cref="AuthConfig"/>.</param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddDashboardAuthentication(Action<AuthConfig> setupAction)
        {
            Argument.IsNotNull(setupAction);

            services.Configure<AuthConfig, AuthConfigValidator>(setupAction);

            return _AddDashboardAuthenticationCore(services);
        }

        /// <summary>
        /// Configures <see cref="AuthConfig"/> using the service provider with startup validation and registers
        /// the scoped <see cref="IAuthService"/>.
        /// </summary>
        /// <param name="setupAction">The delegate that configures <see cref="AuthConfig"/> using the service provider.</param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddDashboardAuthentication(Action<AuthConfig, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            services.Configure<AuthConfig, AuthConfigValidator>(setupAction);

            return _AddDashboardAuthenticationCore(services);
        }
    }

    private static IServiceCollection _AddDashboardAuthenticationCore(IServiceCollection services)
    {
        // AuthService consumes AuthConfig directly (not IOptions<AuthConfig>), so surface the resolved
        // options value as a raw AuthConfig singleton.
        services.AddSingletonOptionValue<AuthConfig>();
        services.TryAddScoped<IAuthService, AuthService>();

        return services;
    }
}
