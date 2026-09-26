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

/// <summary>Extension members on <see cref="IServiceCollection"/> for the Headless CORS policies.</summary>
[PublicAPI]
public static class SetupCors
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the <see cref="HeadlessCorsConstants.RestrictedCors"/> policy from <paramref name="configuration"/>
        /// and the development-only <see cref="HeadlessCorsConstants.AllowAnyCors"/> policy.
        /// </summary>
        /// <param name="configuration">The section bound to <see cref="HeadlessCorsOptions"/>.</param>
        /// <returns>The same service collection.</returns>
        /// <remarks>
        /// Startup fails with an <see cref="OptionsValidationException"/> when the options name no origin, or when
        /// an origin or origin template is not a bare http or https origin. Select a policy with
        /// <c>app.UseCors(HeadlessCorsConstants.RestrictedCors)</c> or <c>RequireCors(...)</c> on an endpoint.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddHeadlessCors(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);
            services.Configure<HeadlessCorsOptions, HeadlessCorsOptionsValidator>(configuration);

            return _AddCorsCore(services);
        }

        /// <summary>
        /// Registers the <see cref="HeadlessCorsConstants.RestrictedCors"/> policy from <see cref="HeadlessCorsOptions"/>
        /// and the development-only <see cref="HeadlessCorsConstants.AllowAnyCors"/> policy.
        /// </summary>
        /// <returns>The same service collection.</returns>
        /// <remarks>Startup fails with an <see cref="OptionsValidationException"/> when the options are invalid.</remarks>
        /// <param name="setupAction">Configures <see cref="HeadlessCorsOptions"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddHeadlessCors(Action<HeadlessCorsOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);
            services.Configure<HeadlessCorsOptions, HeadlessCorsOptionsValidator>(setupAction);

            return _AddCorsCore(services);
        }

        /// <summary>
        /// Registers the <see cref="HeadlessCorsConstants.RestrictedCors"/> policy from <see cref="HeadlessCorsOptions"/>
        /// and the development-only <see cref="HeadlessCorsConstants.AllowAnyCors"/> policy.
        /// </summary>
        /// <returns>The same service collection.</returns>
        /// <remarks>Startup fails with an <see cref="OptionsValidationException"/> when the options are invalid.</remarks>
        /// <param name="setupAction">Configures <see cref="HeadlessCorsOptions"/> with access to the service provider.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddHeadlessCors(Action<HeadlessCorsOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);
            services.Configure<HeadlessCorsOptions, HeadlessCorsOptionsValidator>(setupAction);

            return _AddCorsCore(services);
        }
    }

    private static IServiceCollection _AddCorsCore(IServiceCollection services)
    {
        services.AddCors();

        // Build the policies when CorsOptions resolves, so registration never reads options that later
        // configuration may still change.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<CorsOptions>, ConfigureHeadlessCorsPolicies>()
        );

        return services;
    }
}

internal sealed class ConfigureHeadlessCorsPolicies(IOptions<HeadlessCorsOptions> headlessOptions)
    : IConfigureOptions<CorsOptions>
{
    public void Configure(CorsOptions options)
    {
        var settings = headlessOptions.Value;

        options.AddPolicy(HeadlessCorsConstants.RestrictedCors, policy => _BuildRestricted(policy, settings));

        // Any origin, never credentials: ASP.NET rejects the credentialed combination, and a reflected-origin
        // workaround would hand every site the user's session.
        options.AddPolicy(
            HeadlessCorsConstants.AllowAnyCors,
            static policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
        );
    }

    private static void _BuildRestricted(CorsPolicyBuilder policy, HeadlessCorsOptions settings)
    {
        policy.WithOrigins([.. settings.AllowedOrigins, .. settings.AllowedOriginTemplates]);

        if (settings.AllowedOriginTemplates.Count > 0)
        {
            policy.SetIsOriginAllowedToAllowWildcardSubdomains();
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

        if (settings.AllowCredentials)
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
