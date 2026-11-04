// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
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
/// <see cref="IAuthService"/>, and names the authentication scheme and authorization policy registered with them:
/// the scheme checks the dashboard's own credential through that service, and the policy requires a user the scheme
/// signed in. A dashboard's endpoints carry that policy under Basic, API key, or custom authentication.
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

    /// <summary>
    /// Protects the endpoints of <paramref name="builder"/> with the dashboard authentication registered under
    /// <paramref name="authenticationName"/>, so the host's <c>UseAuthorization</c> evaluates it before the endpoint or
    /// a SignalR hub runs and the host's fallback policy never decides a dashboard request.
    /// </summary>
    /// <remarks>
    /// The mode of the <see cref="AuthConfig"/> named <paramref name="authenticationName"/> decides the rule when the
    /// host builds its endpoints: <see cref="AuthMode.None"/> allows anonymous access; <see cref="AuthMode.Host"/>
    /// requires <see cref="AuthConfig.HostAuthorizationPolicy"/>, or the host's default policy when it is empty; Basic,
    /// API key, and custom modes require a user the dashboard's authentication scheme signed in with the dashboard's
    /// own credential, and refuse any other request with <c>401</c>. Put the rule on the dashboard's protected group
    /// only; endpoints the sign-in flow calls without a credential, such as the shell and <c>/api/auth/info</c>, stay
    /// <c>AllowAnonymous</c>.
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder, such as a route group.</typeparam>
    /// <param name="builder">The endpoints to protect.</param>
    /// <param name="authenticationName">The name passed to <c>AddDashboardAuthentication</c>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="authenticationName"/> is <see langword="null"/>, empty, or white space.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// When the host builds its endpoints: no <c>AddDashboardAuthentication</c> call registered
    /// <paramref name="authenticationName"/>.
    /// </exception>
    public static TBuilder RequireDashboardAuthentication<TBuilder>(this TBuilder builder, string authenticationName)
        where TBuilder : IEndpointConventionBuilder
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNullOrWhiteSpace(authenticationName);

        builder.Add(endpoint =>
        {
            // An unconfigured name reads as a default AuthConfig whose mode is None, which would publish the endpoint
            // openly; AddDashboardAuthentication registers a scheme under every name it configures, so a missing scheme
            // means a typo or a missing registration, and the endpoint must fail closed.
            var schemes = endpoint.ApplicationServices.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
            if (!schemes.SchemeMap.ContainsKey(authenticationName))
            {
                throw new InvalidOperationException(
                    $"No dashboard authentication is registered under '{authenticationName}'. Call "
                        + $"AddDashboardAuthentication(\"{authenticationName}\", ...) before mapping endpoints that "
                        + "require it."
                );
            }

            var auth = endpoint
                .ApplicationServices.GetRequiredService<IOptionsMonitor<AuthConfig>>()
                .Get(authenticationName);

            switch (auth.Mode)
            {
                case AuthMode.None:
                    endpoint.Metadata.Add(new AllowAnonymousAttribute());
                    break;
                case AuthMode.Host:
                    endpoint.Metadata.Add(
                        string.IsNullOrEmpty(auth.HostAuthorizationPolicy)
                            ? new AuthorizeAttribute()
                            : new AuthorizeAttribute(auth.HostAuthorizationPolicy)
                    );
                    break;
                default:
                    endpoint.Metadata.Add(new DashboardEndpointMetadata(authenticationName));
                    endpoint.Metadata.Add(new AuthorizeAttribute(authenticationName));
                    break;
            }
        });

        return builder;
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

        DashboardEndpointAccess.AddDashboardScheme(services, name);

        return services;
    }
}
