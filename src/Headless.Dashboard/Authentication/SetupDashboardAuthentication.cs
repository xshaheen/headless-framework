// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// Provides extension methods for registering dashboard authentication services.
/// </summary>
/// <remarks>
/// Each dashboard registers its authentication under its own name, so two dashboards in one host keep independent
/// modes and credentials. The name selects the named <see cref="AuthConfig"/> options instance and the keyed
/// <see cref="IAuthService"/>, and names the authentication scheme and authorization policy registered with them:
/// the scheme checks the dashboard's own credential through that service, and the policy requires a user the scheme
/// signed in. A dashboard's endpoints carry that policy under Basic or API key authentication. The name also names
/// the host-mode session: the cookie scheme <c>{name}.Session</c> and the policies <c>{name}.View</c> and
/// <c>{name}.Operate</c>, which require a session whose user passes the host's view and operate policies.
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
    /// Ends the host-mode session of the dashboard registered under <paramref name="authenticationName"/> by
    /// deleting its session cookie. Call it from the host's own sign-out so dashboard sessions end with the
    /// host's session; the host's own cookies and tokens are left alone.
    /// </summary>
    /// <param name="context">The host request whose response deletes the cookie.</param>
    /// <param name="authenticationName">The name passed to <c>AddDashboardAuthentication</c>.</param>
    /// <returns>A task that completes when the cookie deletion is written to the response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="authenticationName"/> is <see langword="null"/>, empty, or white space.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No <c>AddDashboardAuthentication</c> call registered <paramref name="authenticationName"/>.
    /// </exception>
    public static Task SignOutHeadlessDashboardAsync(this HttpContext context, string authenticationName)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNullOrWhiteSpace(authenticationName);

        return DashboardHostSession.SignOutAsync(context, authenticationName);
    }

    /// <summary>
    /// Ends the session of the dashboard registered through <c>AddHeadlessDashboard(...)</c> by deleting its session
    /// cookie. Call it from the host's own sign-out so the dashboard session ends with the host's session; the host's
    /// own cookies and tokens are left alone.
    /// </summary>
    /// <param name="context">The host request whose response deletes the cookie.</param>
    /// <returns>A task that completes when the cookie deletion is written to the response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The dashboard is not registered through <c>AddHeadlessDashboard(...)</c>.</exception>
    public static Task SignOutHeadlessDashboardAsync(this HttpContext context)
    {
        Argument.IsNotNull(context);

        return DashboardHostSession.SignOutAsync(context, SetupDashboard.AuthenticationName);
    }

    /// <summary>
    /// Maps the sign-in endpoints of the dashboard registered under <paramref name="authenticationName"/> on
    /// <paramref name="dashboard"/>, the route group at the dashboard's base path: <c>GET api/auth/info</c>,
    /// <c>POST api/auth/session</c>, <c>POST api/auth/sign-out</c>, and <c>GET auth/sign-in</c>.
    /// </summary>
    /// <remarks>
    /// Under <see cref="AuthMode.Host"/>, <see cref="RequireDashboardAuthentication{TBuilder}"/> and
    /// <see cref="RequireDashboardOperation{TBuilder}"/> accept only a dashboard session, and these endpoints are the
    /// only way to get one: <c>api/auth/session</c> exchanges the host's credential, such as a bearer token, and
    /// <c>auth/sign-in</c> signs in the host user, challenging the host's sign-in when there is none. A dashboard that
    /// supports host authentication must map them. <c>api/auth/info</c> reports the mode and the session state to the
    /// dashboard's frontend in every mode; the other three answer <c>404</c> outside host mode. All four are
    /// anonymous, and the two <c>POST</c> endpoints require the <c>X-Requested-With</c> header.
    /// </remarks>
    /// <param name="dashboard">The route group at the dashboard's base path.</param>
    /// <param name="authenticationName">The name passed to <c>AddDashboardAuthentication</c>.</param>
    /// <returns>The same <paramref name="dashboard"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dashboard"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="authenticationName"/> is <see langword="null"/>, empty, or white space.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No <c>AddDashboardAuthentication</c> call registered <paramref name="authenticationName"/>.
    /// </exception>
    public static IEndpointRouteBuilder MapDashboardAuthEndpoints(
        this IEndpointRouteBuilder dashboard,
        string authenticationName
    )
    {
        Argument.IsNotNull(dashboard);
        Argument.IsNotNullOrWhiteSpace(authenticationName);

        _EnsureRegistered(dashboard.ServiceProvider, authenticationName);
        DashboardAuthEndpoints.Map(dashboard, authenticationName);

        return dashboard;
    }

    /// <summary>
    /// Protects the endpoints of <paramref name="builder"/> with the dashboard authentication registered under
    /// <paramref name="authenticationName"/>, so the host's <c>UseAuthorization</c> evaluates it before the endpoint or
    /// a SignalR hub runs and the host's fallback policy never decides a dashboard request.
    /// </summary>
    /// <remarks>
    /// The mode of the <see cref="AuthConfig"/> named <paramref name="authenticationName"/> decides the rule when the
    /// host builds its endpoints: <see cref="AuthMode.None"/> allows anonymous access; Basic and API key modes require
    /// a user the dashboard's authentication scheme signed in with the dashboard's own credential, and refuse any other
    /// request with <c>401</c>. Under <see cref="AuthMode.Host"/>, it requires a dashboard session whose user passes
    /// <see cref="DashboardHostAuthenticationOptions.ViewPolicy"/>, or the host's default policy when it is empty, and
    /// answers <c>401</c> without a session and <c>403</c> when the policy refuses; the host's own sign-in does not
    /// open the endpoint, so the dashboard must map <see cref="MapDashboardAuthEndpoints"/> to issue that session. Put
    /// the rule on the dashboard's protected group only; endpoints the sign-in flow calls without a credential, such as
    /// the shell and <c>/api/auth/info</c>, stay <c>AllowAnonymous</c>.
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
            var auth = _GetRegisteredConfig(endpoint, authenticationName);

            switch (auth.Mode)
            {
                case AuthMode.None:
                    endpoint.Metadata.Add(new AllowAnonymousAttribute());
                    break;
                case AuthMode.Host:
                    endpoint.Metadata.Add(
                        new AuthorizeAttribute(DashboardHostSession.ViewPolicyName(authenticationName))
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

    /// <summary>
    /// Marks the endpoints of <paramref name="builder"/> as operator actions of the dashboard registered under
    /// <paramref name="authenticationName"/>. Apply it beside <see cref="RequireDashboardAuthentication{TBuilder}"/>.
    /// </summary>
    /// <remarks>
    /// Under <see cref="AuthMode.Host"/>, the session's user must also pass <see cref="DashboardHostAuthenticationOptions.OperatePolicy"/>, or the view policy when it is empty, and is
    /// refused with <c>403</c> otherwise. Other modes add nothing: Basic and API key share one credential for viewing
    /// and operating, and <see cref="AuthMode.None"/> is open.
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder, such as a route handler.</typeparam>
    /// <param name="builder">The operator-action endpoints.</param>
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
    public static TBuilder RequireDashboardOperation<TBuilder>(this TBuilder builder, string authenticationName)
        where TBuilder : IEndpointConventionBuilder
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNullOrWhiteSpace(authenticationName);

        builder.Add(endpoint =>
        {
            var auth = _GetRegisteredConfig(endpoint, authenticationName);

            if (auth.Mode == AuthMode.Host)
            {
                endpoint.Metadata.Add(
                    new AuthorizeAttribute(DashboardHostSession.OperatePolicyName(authenticationName))
                );
            }
        });

        return builder;
    }

    private static AuthConfig _GetRegisteredConfig(EndpointBuilder endpoint, string authenticationName)
    {
        _EnsureRegistered(endpoint.ApplicationServices, authenticationName);

        return endpoint.ApplicationServices.GetRequiredService<IOptionsMonitor<AuthConfig>>().Get(authenticationName);
    }

    private static void _EnsureRegistered(IServiceProvider services, string authenticationName)
    {
        // An unconfigured name reads as a default AuthConfig whose mode is None, which would publish the endpoint
        // openly; AddDashboardAuthentication registers a scheme under every name it configures, so a missing scheme
        // means a typo or a missing registration, and the endpoint must fail closed.
        var schemes = services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        if (!schemes.SchemeMap.ContainsKey(authenticationName))
        {
            throw new InvalidOperationException(
                $"No dashboard authentication is registered under '{authenticationName}'. Call "
                    + $"AddDashboardAuthentication(\"{authenticationName}\", ...) before mapping endpoints that "
                    + "require it."
            );
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

        DashboardEndpointAccess.AddDashboardScheme(services, name);
        DashboardHostSession.AddSessionScheme(services, name);
        DashboardHostAuthorization.AddSessionPolicies(services, name);

        return services;
    }
}
