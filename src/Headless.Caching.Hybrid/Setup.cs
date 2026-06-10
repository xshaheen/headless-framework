// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Caching;

/// <summary>DI registration extension methods for hybrid cache.</summary>
[PublicAPI]
public static class SetupHybridCache
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds hybrid cache services with the specified configuration.
        /// </summary>
        /// <param name="setupAction">Configuration action for <see cref="HybridCacheOptions"/>.</param>
        /// <param name="isDefault">
        /// When true, registers <see cref="HybridCache"/> as the default <see cref="ICache"/> implementation.
        /// </param>
        /// <returns>The service collection for chaining.</returns>
        /// <remarks>
        /// <para><b>Prerequisites:</b></para>
        /// <list type="bullet">
        /// <item>In-memory cache: Call <c>AddInMemoryCache()</c> before this</item>
        /// <item>Distributed cache: Call <c>AddRedisCache()</c> or similar before this</item>
        /// <item>Messaging: Configure messaging with <c>AddHeadlessMessaging()</c></item>
        /// </list>
        /// <para><b>Example:</b></para>
        /// <code>
        /// services.AddInMemoryCache(isDefault: false);
        /// services.AddRedisCache(options => options.ConnectionString = "localhost:6379");
        /// services.AddHeadlessMessaging(...);
        /// services.AddHybridCache(options =>
        /// {
        ///     options.DefaultLocalExpiration = TimeSpan.FromMinutes(5);
        /// });
        /// </code>
        /// <para>
        /// When <see cref="HybridCacheOptions.LocalCacheName"/> / <see cref="HybridCacheOptions.RemoteCacheName"/>
        /// are set, the corresponding tier is resolved from the keyed <see cref="ICache"/> registration with that
        /// name instead of the default <see cref="IInMemoryCache"/> / <see cref="IRemoteCache"/> services.
        /// </para>
        /// </remarks>
        public IServiceCollection AddHybridCache(Action<HybridCacheOptions>? setupAction = null, bool isDefault = true)
        {
            if (setupAction is null)
            {
                services.AddOptions<HybridCacheOptions, HybridCacheOptionsValidator>();
            }
            else
            {
                services.Configure<HybridCacheOptions, HybridCacheOptionsValidator>(setupAction);
            }

            return services._AddCacheCore(isDefault);
        }

        /// <summary>
        /// Adds hybrid cache services with service provider-aware configuration.
        /// </summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <param name="isDefault">
        /// When true, registers <see cref="HybridCache"/> as the default <see cref="ICache"/> implementation.
        /// </param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddHybridCache(
            Action<HybridCacheOptions, IServiceProvider> setupAction,
            bool isDefault = true
        )
        {
            services.Configure<HybridCacheOptions, HybridCacheOptionsValidator>(setupAction);

            return services._AddCacheCore(isDefault);
        }

        /// <summary>
        /// Adds an independently-configured named hybrid cache instance, resolvable as a keyed
        /// <see cref="ICache"/> service or through <see cref="ICacheProvider"/>. Tiers are resolved per
        /// <see cref="HybridCacheOptions.LocalCacheName"/> / <see cref="HybridCacheOptions.RemoteCacheName"/>
        /// (falling back to the default <see cref="IInMemoryCache"/> / <see cref="IRemoteCache"/> when unset).
        /// Named instances never touch the default (unkeyed) <see cref="ICache"/> nor the reserved role keys.
        /// </summary>
        /// <param name="name">The cache instance name. Must be non-empty and not a reserved role key.</param>
        /// <param name="setupAction">Configuration action for the instance's <see cref="HybridCacheOptions"/>.</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddHybridCache(string name, Action<HybridCacheOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            return services._AddNamedCache(name, (options, _) => setupAction(options));
        }

        /// <summary>
        /// Adds an independently-configured named hybrid cache instance with service provider-aware
        /// configuration. See <c>AddHybridCache(string, Action&lt;HybridCacheOptions&gt;)</c>.
        /// </summary>
        /// <param name="name">The cache instance name. Must be non-empty and not a reserved role key.</param>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddHybridCache(string name, Action<HybridCacheOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            return services._AddNamedCache(name, setupAction);
        }

        private IServiceCollection _AddNamedCache(string name, Action<HybridCacheOptions, IServiceProvider> setupAction)
        {
            _EnsureValidInstanceName(name);

            services.Configure<HybridCacheOptions, HybridCacheOptionsValidator>(setupAction, name);
            services.AddCacheProvider();

            services.AddKeyedSingleton<ICache>(
                name,
                (provider, _) =>
                    _CreateHybridCache(
                        provider,
                        provider.GetRequiredService<IOptionsMonitor<HybridCacheOptions>>().Get(name)
                    )
            );

            return services;
        }

        private IServiceCollection _AddCacheCore(bool isDefault)
        {
            services.AddSingletonOptionValue<HybridCacheOptions>();
            services.TryAddSingleton<HybridCache>(provider =>
                _CreateHybridCache(provider, provider.GetRequiredService<HybridCacheOptions>())
            );
            services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));
            services.AddCacheProvider();

            if (!isDefault)
            {
                services.AddKeyedSingleton<ICache>(
                    CacheConstants.HybridCacheProvider,
                    (provider, _) => provider.GetRequiredService<HybridCache>()
                );
            }
            else
            {
                services.TryAddSingleton<ICache>(provider => provider.GetRequiredService<HybridCache>());
                services.AddKeyedSingleton(
                    CacheConstants.HybridCacheProvider,
                    (x, _) => x.GetRequiredService<ICache>()
                );
            }

            return services;
        }
    }

    private static HybridCache _CreateHybridCache(IServiceProvider provider, HybridCacheOptions options)
    {
        var l1Cache = _ResolveTier<IInMemoryCache>(
            provider,
            options.LocalCacheName,
            nameof(HybridCacheOptions.LocalCacheName),
            "AddInMemoryCache(name, …)"
        );

        var l2Cache = _ResolveTier<IRemoteCache>(
            provider,
            options.RemoteCacheName,
            nameof(HybridCacheOptions.RemoteCacheName),
            "AddRedisCache(name, …)"
        );

        return new HybridCache(
            l1Cache,
            l2Cache,
            provider.GetRequiredService<IBus>(),
            options,
            provider.GetService<ILogger<HybridCache>>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<ICacheFactoryLockProvider>()
        );
    }

    private static TTier _ResolveTier<TTier>(
        IServiceProvider provider,
        string? name,
        string optionName,
        string registrationHint
    )
        where TTier : class, ICache
    {
        if (name is null)
        {
            return provider.GetRequiredService<TTier>();
        }

        var cache =
            provider.GetKeyedService<ICache>(name)
            ?? throw new InvalidOperationException(
                $"{nameof(HybridCacheOptions)}.{optionName} is set to '{name}', but no cache is registered "
                    + $"under that name. Register the named instance first, for example {registrationHint}."
            );

        return cache as TTier
            ?? throw new InvalidOperationException(
                $"{nameof(HybridCacheOptions)}.{optionName} is set to '{name}', but the cache registered under "
                    + $"that name ({cache.GetType().Name}) does not implement {typeof(TTier).Name}. Register the "
                    + $"named tier with {registrationHint}."
            );
    }

    private static void _EnsureValidInstanceName(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        if (CacheConstants.IsReservedProviderKey(name))
        {
            throw new ArgumentException(
                $"The cache name '{name}' is reserved for the role-keyed registrations "
                    + $"('{CacheConstants.MemoryCacheProvider}', '{CacheConstants.RemoteCacheProvider}', "
                    + $"'{CacheConstants.HybridCacheProvider}'). Pick a different instance name.",
                nameof(name)
            );
        }
    }
}
