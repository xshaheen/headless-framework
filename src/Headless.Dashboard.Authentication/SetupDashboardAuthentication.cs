// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Registration extensions for dashboard authentication (<see cref="AuthConfig"/> + <see cref="IAuthService"/>).
/// </summary>
[PublicAPI]
public static class SetupDashboardAuthentication
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Binds <see cref="AuthConfig"/> from <paramref name="configuration"/> (validated on start via
        /// <c>AuthConfigValidator</c>) and registers the scoped <see cref="IAuthService"/>.
        /// </summary>
        /// <param name="configuration">Configuration section to bind into <see cref="AuthConfig"/>.</param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddDashboardAuthentication(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            services.Configure<AuthConfig, AuthConfigValidator>(configuration);

            return _AddDashboardAuthenticationCore(services);
        }

        /// <summary>
        /// Configures <see cref="AuthConfig"/> via <paramref name="setupAction"/> (validated on start via
        /// <c>AuthConfigValidator</c>) and registers the scoped <see cref="IAuthService"/>.
        /// </summary>
        /// <param name="setupAction">Delegate that configures <see cref="AuthConfig"/>.</param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddDashboardAuthentication(Action<AuthConfig> setupAction)
        {
            Argument.IsNotNull(setupAction);

            services.Configure<AuthConfig, AuthConfigValidator>(setupAction);

            return _AddDashboardAuthenticationCore(services);
        }

        /// <summary>
        /// Configures <see cref="AuthConfig"/> via a delegate that also receives the
        /// <see cref="IServiceProvider"/> (validated on start via <c>AuthConfigValidator</c>) and registers
        /// the scoped <see cref="IAuthService"/>.
        /// </summary>
        /// <param name="setupAction">Delegate that configures <see cref="AuthConfig"/> with access to the DI container.</param>
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
