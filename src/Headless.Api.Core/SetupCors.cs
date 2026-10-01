// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Cors;
using Headless.Checks;
using Headless.Constants;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>Extension members on <see cref="IServiceCollection"/> for Headless CORS policies.</summary>
/// <remarks>
/// ASP.NET Core's <c>AddCors</c> stays the way to write a policy in code. These members add what it lacks: a policy
/// bound from configuration and validated at startup (<c>AddHeadlessCors</c>), and origins approved at request time
/// for any policy (<c>AddHeadlessCorsOriginSource</c>). Select a policy with <c>app.UseCors(name)</c> or
/// <c>RequireCors(name)</c>.
/// </remarks>
[PublicAPI]
public static class SetupCors
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the <see cref="HeadlessCorsConstants.RestrictedCors"/> policy from <paramref name="configuration"/>.
        /// </summary>
        /// <param name="configuration">The section bound to <see cref="HeadlessCorsOptions"/>.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the policy is already registered.</exception>
        public IServiceCollection AddHeadlessCors(IConfiguration configuration)
        {
            return services.AddHeadlessCors(HeadlessCorsConstants.RestrictedCors, configuration);
        }

        /// <summary>Registers the <see cref="HeadlessCorsConstants.RestrictedCors"/> policy.</summary>
        /// <param name="setupAction">Configures <see cref="HeadlessCorsOptions"/>.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the policy is already registered.</exception>
        public IServiceCollection AddHeadlessCors(Action<HeadlessCorsOptions> setupAction)
        {
            return services.AddHeadlessCors(HeadlessCorsConstants.RestrictedCors, setupAction);
        }

        /// <summary>Registers the CORS policy <paramref name="policyName"/> from <paramref name="configuration"/>.</summary>
        /// <param name="policyName">The policy name passed to <c>UseCors</c> or <c>RequireCors</c>.</param>
        /// <param name="configuration">The section bound to <see cref="HeadlessCorsOptions"/>.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="policyName"/> is blank.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the policy is already registered.</exception>
        public IServiceCollection AddHeadlessCors(string policyName, IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);
            _AddPolicy(services, policyName).Bind(configuration);

            return services;
        }

        /// <summary>Registers the CORS policy <paramref name="policyName"/>.</summary>
        /// <param name="policyName">The policy name passed to <c>UseCors</c> or <c>RequireCors</c>.</param>
        /// <param name="setupAction">Configures <see cref="HeadlessCorsOptions"/>.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="policyName"/> is blank.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the policy is already registered.</exception>
        public IServiceCollection AddHeadlessCors(string policyName, Action<HeadlessCorsOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);
            _AddPolicy(services, policyName).Configure(setupAction);

            return services;
        }

        /// <summary>
        /// Registers the <see cref="HeadlessCorsConstants.AllowAnyCors"/> policy: any origin, never credentials, and
        /// by default any header and method.
        /// </summary>
        /// <param name="setupAction">Optionally configures the policy's headers, methods, exposed headers, and max age.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the policy is already registered.</exception>
        /// <remarks>
        /// Meant for development. Startup fails in every other environment unless
        /// <see cref="HeadlessCorsOptions.AllowAnyOriginOutsideDevelopment"/> is set, which a public API that any site
        /// may call without credentials does deliberately.
        /// </remarks>
        public IServiceCollection AddHeadlessAllowAnyCors(Action<HeadlessCorsOptions>? setupAction = null)
        {
            return services.AddHeadlessCors(
                HeadlessCorsConstants.AllowAnyCors,
                options =>
                {
                    options.AllowAnyOrigin = true;
                    setupAction?.Invoke(options);
                }
            );
        }

        /// <summary>
        /// Registers <typeparamref name="TSource"/> to approve origins for <paramref name="policyName"/> at request
        /// time, beyond the policy's static origins.
        /// </summary>
        /// <typeparam name="TSource">The origin source, such as one reading tenant custom domains.</typeparam>
        /// <param name="policyName">The policy the source serves. Defaults to <see cref="HeadlessCorsConstants.RestrictedCors"/>.</param>
        /// <param name="lifetime">The source's lifetime. Defaults to scoped, resolved from the request's services.</param>
        /// <returns>The same service collection.</returns>
        /// <remarks>
        /// The policy may come from <c>AddCors</c> or <c>AddHeadlessCors</c>; one registered with
        /// <c>AddHeadlessCors</c> may then leave its static origin lists empty. A later registration for the same
        /// policy replaces an earlier one. The source decorates the registered <see cref="ICorsPolicyProvider"/>, so
        /// register a custom provider before this call; one registered afterwards replaces the decorator.
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when <paramref name="policyName"/> is blank.</exception>
        public IServiceCollection AddHeadlessCorsOriginSource<TSource>(
            string policyName = HeadlessCorsConstants.RestrictedCors,
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
            where TSource : class, ICorsOriginSource
        {
            Argument.IsNotNullOrWhiteSpace(policyName);

            // AddCors registers the ICorsPolicyProvider the source decorates.
            services.AddCors();

            // Harmless for a policy written with AddCors; a Headless policy reads it to accept empty origin lists.
            services.Configure<HeadlessCorsOptions>(policyName, static options => options.HasOriginSource = true);
            services.Add(
                ServiceDescriptor.DescribeKeyed(typeof(ICorsOriginSource), policyName, typeof(TSource), lifetime)
            );

            if (!services.IsAdded<HeadlessCorsPolicyProviderMarker>())
            {
                services.AddSingleton(new HeadlessCorsPolicyProviderMarker());
                services.TryDecorate<ICorsPolicyProvider, HeadlessCorsPolicyProvider>();
            }

            return services;
        }
    }

    private static OptionsBuilder<HeadlessCorsOptions> _AddPolicy(IServiceCollection services, string policyName)
    {
        Argument.IsNotNullOrWhiteSpace(policyName);

        // Each registration adds its own validator, so a second call for one name would report every failure twice
        // and leave unclear which call owns the policy.
        if (services.Any(d => d.ServiceType == typeof(HeadlessCorsPolicyMarker) && Equals(d.ServiceKey, policyName)))
        {
            throw new InvalidOperationException(
                $"The CORS policy '{policyName}' is already registered. Configure it in a single AddHeadlessCors call."
            );
        }

        services.AddKeyedSingleton(policyName, new HeadlessCorsPolicyMarker());

        // Build the policy when CorsOptions resolves, so registration never reads options that later configuration
        // may still change.
        services
            .AddOptions<CorsOptions>()
            .Configure<IOptionsMonitor<HeadlessCorsOptions>>(
                (cors, headless) => cors.AddPolicy(policyName, policy => _Build(policy, headless.Get(policyName)))
            );

        return services.AddCors().AddOptions<HeadlessCorsOptions, HeadlessCorsOptionsValidator>(policyName);
    }

    private static void _Build(CorsPolicyBuilder policy, HeadlessCorsOptions settings)
    {
        if (settings.AllowAnyOrigin)
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins([.. settings.AllowedOrigins, .. settings.AllowedOriginTemplates]);

            if (settings.AllowedOriginTemplates.Count > 0)
            {
                policy.SetIsOriginAllowedToAllowWildcardSubdomains();
            }
        }

        if (_IsAny(settings.AllowedHeaders))
        {
            policy.AllowAnyHeader();
        }
        else
        {
            policy.WithHeaders([.. settings.AllowedHeaders]);
        }

        if (_IsAny(settings.AllowedMethods))
        {
            policy.AllowAnyMethod();
        }
        else
        {
            policy.WithMethods([.. settings.AllowedMethods]);
        }

        IEnumerable<string> exposed = settings.ExposeFrameworkHeaders
            ? settings.ExposedHeaders.Union(
                HeadlessCorsOptions.FrameworkExposedHeaders,
                StringComparer.OrdinalIgnoreCase
            )
            : settings.ExposedHeaders;

        policy.WithExposedHeaders([.. exposed]);
        policy.SetPreflightMaxAge(settings.MaxAge);

        // The validator already refuses credentials on an any-origin policy; the guard keeps the builder from ever
        // producing the combination ASP.NET rejects at request time.
        if (settings.AllowCredentials && !settings.AllowAnyOrigin)
        {
            policy.AllowCredentials();
        }
        else
        {
            policy.DisallowCredentials();
        }
    }

    private static bool _IsAny(List<string> values)
    {
        return values.Count == 0 || values.Contains("*", StringComparer.Ordinal);
    }
}

/// <summary>Marks a policy name registered through <c>AddHeadlessCors</c>, keyed by that name.</summary>
internal sealed class HeadlessCorsPolicyMarker;

internal sealed class HeadlessCorsPolicyProviderMarker;
