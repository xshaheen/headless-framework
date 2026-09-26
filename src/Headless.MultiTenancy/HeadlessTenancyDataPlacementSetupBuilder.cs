// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.MultiTenancy;

/// <summary>
/// Fluent builder passed to the <c>HeadlessTenancyBuilder.DataPlacement(...)</c> delegate; selects exactly one
/// placement source and configures <see cref="TenantDataPlacementOptions"/>.
/// </summary>
[PublicAPI]
public sealed class HeadlessTenancyDataPlacementSetupBuilder
{
    internal const string ConfigurationSource = "configuration";
    internal const string ResolverSource = "resolver";

    internal HeadlessTenancyDataPlacementSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal List<(string Label, Action<IServiceCollection> Register)> Sources { get; } = [];

    /// <summary>Applies a configuration delegate to <see cref="TenantDataPlacementOptions"/>.</summary>
    /// <param name="configure">The delegate that mutates <see cref="TenantDataPlacementOptions"/>.</param>
    /// <returns>The same builder instance to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessTenancyDataPlacementSetupBuilder Configure(Action<TenantDataPlacementOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure<TenantDataPlacementOptions, TenantDataPlacementOptionsValidator>(configure);

        return this;
    }

    /// <summary>
    /// Resolves placements from configuration, binding <see cref="ConfigurationTenantDataPlacementOptions"/> once at
    /// startup from <paramref name="configuration"/> (for example the <c>Headless:MultiTenancy:DataPlacement</c>
    /// section). A change requires a process restart.
    /// </summary>
    /// <param name="configuration">The configuration to bind.</param>
    /// <returns>The same builder instance to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    public HeadlessTenancyDataPlacementSetupBuilder UseConfiguration(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        return _AddConfigurationSource(services =>
            services.Configure<
                ConfigurationTenantDataPlacementOptions,
                ConfigurationTenantDataPlacementOptionsValidator
            >(configuration)
        );
    }

    /// <summary>Resolves placements from options populated by <paramref name="configure"/>.</summary>
    /// <param name="configure">Delegate that adds <see cref="ConfigurationTenantDataPlacement"/> entries.</param>
    /// <returns>The same builder instance to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessTenancyDataPlacementSetupBuilder UseConfiguration(
        Action<ConfigurationTenantDataPlacementOptions> configure
    )
    {
        Argument.IsNotNull(configure);

        return _AddConfigurationSource(services =>
            services.Configure<
                ConfigurationTenantDataPlacementOptions,
                ConfigurationTenantDataPlacementOptionsValidator
            >(configure)
        );
    }

    /// <summary>
    /// Resolves placements from options populated by <paramref name="configure"/> with access to the
    /// <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="configure">Delegate that adds <see cref="ConfigurationTenantDataPlacement"/> entries.</param>
    /// <returns>The same builder instance to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessTenancyDataPlacementSetupBuilder UseConfiguration(
        Action<ConfigurationTenantDataPlacementOptions, IServiceProvider> configure
    )
    {
        Argument.IsNotNull(configure);

        return _AddConfigurationSource(services =>
            services.Configure<
                ConfigurationTenantDataPlacementOptions,
                ConfigurationTenantDataPlacementOptionsValidator
            >(configure)
        );
    }

    /// <summary>
    /// Resolves placements through an app-supplied <typeparamref name="TResolver"/>, registered as scoped and
    /// cached in process for <see cref="TenantDataPlacementOptions.CacheExpiration"/>. Requires an in-process cache
    /// tier (<c>AddHeadlessCaching(c =&gt; c.UseInMemory())</c> or a hybrid cache): placements carry connection
    /// strings and are never written to a distributed cache.
    /// </summary>
    /// <typeparam name="TResolver">The resolver. It must read host-level storage only.</typeparam>
    /// <returns>The same builder instance to allow chaining.</returns>
    public HeadlessTenancyDataPlacementSetupBuilder UseResolver<TResolver>()
        where TResolver : class, ITenantDataPlacementResolver
    {
        Sources.Add(
            (
                ResolverSource,
                services =>
                {
                    services.TryAddScoped<TResolver>();
                    services.Replace(
                        ServiceDescriptor.Scoped<
                            ITenantDataPlacementResolver,
                            CachingTenantDataPlacementResolver<TResolver>
                        >()
                    );
                    services.RequireRegisteredService<IInMemoryCache>(
                        "Headless multi-tenancy data placement (UseResolver)",
                        "Call AddHeadlessCaching(...) with an in-process tier (UseInMemory or UseHybrid); tenant "
                            + "placements carry connection strings and are cached in process only."
                    );
                }
            )
        );

        return this;
    }

    private HeadlessTenancyDataPlacementSetupBuilder _AddConfigurationSource(Action<IServiceCollection> bindOptions)
    {
        Sources.Add(
            (
                ConfigurationSource,
                services =>
                {
                    bindOptions(services);
                    services.Replace(
                        ServiceDescriptor.Singleton<
                            ITenantDataPlacementResolver,
                            ConfigurationTenantDataPlacementResolver
                        >()
                    );
                }
            )
        );

        return this;
    }
}
