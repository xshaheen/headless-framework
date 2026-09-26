// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Cors;
using Headless.Checks;
using Headless.Constants;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>Extension members on <see cref="IServiceCollection"/> for Headless CORS policies.</summary>
/// <remarks>
/// Every policy is a named <see cref="HeadlessCorsOptions"/> instance validated at startup; startup fails with an
/// <see cref="OptionsValidationException"/> on an invalid one. Calling a registration again for the same name adds
/// to that policy's configuration. Select a policy with <c>app.UseCors(name)</c> or <c>RequireCors(name)</c>.
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
        public IServiceCollection AddHeadlessCors(IConfiguration configuration)
        {
            return services.AddHeadlessCors(HeadlessCorsConstants.RestrictedCors, configuration);
        }

        /// <summary>Registers the <see cref="HeadlessCorsConstants.RestrictedCors"/> policy.</summary>
        /// <param name="setupAction">Configures <see cref="HeadlessCorsOptions"/>.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddHeadlessCors(Action<HeadlessCorsOptions> setupAction)
        {
            return services.AddHeadlessCors(HeadlessCorsConstants.RestrictedCors, setupAction);
        }

        /// <summary>Registers the <see cref="HeadlessCorsConstants.RestrictedCors"/> policy.</summary>
        /// <param name="setupAction">Configures <see cref="HeadlessCorsOptions"/> with access to the service provider.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddHeadlessCors(Action<HeadlessCorsOptions, IServiceProvider> setupAction)
        {
            return services.AddHeadlessCors(HeadlessCorsConstants.RestrictedCors, setupAction);
        }

        /// <summary>Registers the CORS policy <paramref name="policyName"/> from <paramref name="configuration"/>.</summary>
        /// <param name="policyName">The policy name passed to <c>UseCors</c> or <c>RequireCors</c>.</param>
        /// <param name="configuration">The section bound to <see cref="HeadlessCorsOptions"/>.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="policyName"/> is blank.</exception>
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
        public IServiceCollection AddHeadlessCors(string policyName, Action<HeadlessCorsOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);
            _AddPolicy(services, policyName).Configure(setupAction);

            return services;
        }

        /// <summary>Registers the CORS policy <paramref name="policyName"/>.</summary>
        /// <param name="policyName">The policy name passed to <c>UseCors</c> or <c>RequireCors</c>.</param>
        /// <param name="setupAction">Configures <see cref="HeadlessCorsOptions"/> with access to the service provider.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="policyName"/> is blank.</exception>
        public IServiceCollection AddHeadlessCors(
            string policyName,
            Action<HeadlessCorsOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);
            _AddPolicy(services, policyName).Configure<IServiceProvider>((options, sp) => setupAction(options, sp));

            return services;
        }

        /// <summary>
        /// Registers the <see cref="HeadlessCorsConstants.AllowAnyCors"/> policy: any origin, never credentials, and
        /// by default any header and method.
        /// </summary>
        /// <param name="setupAction">Optionally configures the policy's headers, methods, exposed headers, and max age.</param>
        /// <returns>The same service collection.</returns>
        /// <remarks>
        /// Meant for development. Startup fails in the Production environment unless
        /// <see cref="HeadlessCorsOptions.AllowAnyOriginInProduction"/> is set, which a public API that any site may
        /// call without credentials does deliberately.
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
        /// time, beyond its static <see cref="HeadlessCorsOptions.AllowedOrigins"/>.
        /// </summary>
        /// <typeparam name="TSource">The origin source, such as one reading tenant custom domains.</typeparam>
        /// <param name="policyName">The policy the source serves. Defaults to <see cref="HeadlessCorsConstants.RestrictedCors"/>.</param>
        /// <param name="lifetime">The source's lifetime. Defaults to scoped, resolved from the request's services.</param>
        /// <returns>The same service collection.</returns>
        /// <remarks>
        /// The policy may then leave its static origin lists empty. A later registration for the same policy replaces
        /// an earlier one. The source decorates the registered <see cref="ICorsPolicyProvider"/>, so register a custom
        /// provider before this call; one registered afterwards replaces the decorator.
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when <paramref name="policyName"/> is blank.</exception>
        public IServiceCollection AddHeadlessCorsOriginSource<TSource>(
            string policyName = HeadlessCorsConstants.RestrictedCors,
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
            where TSource : class, ICorsOriginSource
        {
            _AddPolicy(services, policyName).Configure(static options => options.HasOriginSource = true);
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

        services.AddCors();

        // Build every policy when CorsOptions resolves, so registration never reads options that later
        // configuration may still change.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<CorsOptions>, ConfigureHeadlessCorsPolicies>()
        );

        var registry = _GetRegistry(services);

        // One validator per name: ValidateFluentValidation adds a validation registration on each call, so a second
        // call for the same policy would report every failure twice.
        if (registry.Names.Add(policyName))
        {
            return services.AddOptions<HeadlessCorsOptions, HeadlessCorsOptionsValidator>(policyName);
        }

        return services.AddOptions<HeadlessCorsOptions>(policyName);
    }

    private static HeadlessCorsPolicyRegistry _GetRegistry(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(HeadlessCorsPolicyRegistry) && !descriptor.IsKeyedService)
            {
                return (HeadlessCorsPolicyRegistry)descriptor.ImplementationInstance!;
            }
        }

        var registry = new HeadlessCorsPolicyRegistry();
        services.AddSingleton(registry);

        return registry;
    }
}

/// <summary>The names of every policy registered through <c>AddHeadlessCors</c>.</summary>
internal sealed class HeadlessCorsPolicyRegistry
{
    public HashSet<string> Names { get; } = new(StringComparer.Ordinal);
}

internal sealed class HeadlessCorsPolicyProviderMarker;

internal sealed class ConfigureHeadlessCorsPolicies(
    HeadlessCorsPolicyRegistry registry,
    IOptionsMonitor<HeadlessCorsOptions> headlessOptions
) : IConfigureOptions<CorsOptions>
{
    public void Configure(CorsOptions options)
    {
        foreach (var name in registry.Names)
        {
            var settings = headlessOptions.Get(name);
            options.AddPolicy(name, policy => _Build(policy, settings));
        }
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

        if (settings.ExposedHeaders.Count > 0)
        {
            policy.WithExposedHeaders([.. settings.ExposedHeaders]);
        }

        if (settings.MaxAge is { } maxAge)
        {
            policy.SetPreflightMaxAge(maxAge);
        }

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
