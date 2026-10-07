// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Provides extension methods for registering dashboard authentication services.
/// </summary>
/// <remarks>
/// Each dashboard registers its authentication under its own name, so two dashboards in one host keep independent
/// modes and credentials. The name selects the named <see cref="AuthConfig"/> options instance and the keyed
/// <see cref="IAuthService"/>; pass the same name to <see cref="AuthMiddleware"/>.
/// </remarks>
[PublicAPI]
public static class SetupDashboardAuthentication
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Binds the <see cref="AuthConfig"/> named <paramref name="name"/> from <paramref name="configuration"/> with
        /// startup validation and registers the scoped <see cref="IAuthService"/> keyed by <paramref name="name"/>.
        /// </summary>
        /// <param name="name">The dashboard's authentication name: the options name and the service key.</param>
        /// <param name="configuration">The configuration section to bind into <see cref="AuthConfig"/>.</param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty, or white space.</exception>
        public IServiceCollection AddDashboardAuthentication(string name, IConfiguration configuration)
        {
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(configuration);

            services.Configure<AuthConfig, AuthConfigValidator>(configuration, name);

            return _AddDashboardAuthenticationCore(services, name);
        }

        /// <summary>
        /// Configures the <see cref="AuthConfig"/> named <paramref name="name"/> with startup validation and registers
        /// the scoped <see cref="IAuthService"/> keyed by <paramref name="name"/>.
        /// </summary>
        /// <param name="name">The dashboard's authentication name: the options name and the service key.</param>
        /// <param name="setupAction">The delegate that configures <see cref="AuthConfig"/>.</param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty, or white space.</exception>
        public IServiceCollection AddDashboardAuthentication(string name, Action<AuthConfig> setupAction)
        {
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(setupAction);

            services.Configure<AuthConfig, AuthConfigValidator>(setupAction, name);

            return _AddDashboardAuthenticationCore(services, name);
        }

        /// <summary>
        /// Configures the <see cref="AuthConfig"/> named <paramref name="name"/> using the service provider with
        /// startup validation and registers the scoped <see cref="IAuthService"/> keyed by <paramref name="name"/>.
        /// </summary>
        /// <param name="name">The dashboard's authentication name: the options name and the service key.</param>
        /// <param name="setupAction">The delegate that configures <see cref="AuthConfig"/> using the service provider.</param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty, or white space.</exception>
        public IServiceCollection AddDashboardAuthentication(
            string name,
            Action<AuthConfig, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(setupAction);

            services.Configure<AuthConfig, AuthConfigValidator>(setupAction, name);

            return _AddDashboardAuthenticationCore(services, name);
        }
    }

    private static IServiceCollection _AddDashboardAuthenticationCore(IServiceCollection services, string name)
    {
        // TryAdd keeps a consumer's own IAuthService registered under the same key ahead of this default.
        services.TryAddKeyedScoped<IAuthService>(
            name,
            static (serviceProvider, key) =>
                new AuthService(
                    serviceProvider.GetRequiredService<IOptionsMonitor<AuthConfig>>().Get((string)key!),
                    serviceProvider.GetRequiredService<ILogger<AuthService>>()
                )
        );

        return services;
    }
}
