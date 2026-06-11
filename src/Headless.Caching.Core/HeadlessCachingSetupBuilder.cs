// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Caching;

/// <summary>
/// Root builder for <c>AddHeadlessCaching</c>. Provider packages contribute deferred service registrations into
/// four slots — default (exactly one), role-keyed tiers (at most one per reserved role), named instances
/// (unlimited, unique names), and cross-cutting extensions. Nothing is registered into
/// <see cref="Services"/> until the setup gates pass; contributions are queued only.
/// </summary>
[PublicAPI]
public sealed class HeadlessCachingSetupBuilder
{
    private readonly HashSet<string> _instanceNames = new(StringComparer.Ordinal);

    internal HeadlessCachingSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal List<(string RoleKey, ICacheProviderOptionsExtension Extension)> DefaultExtensions { get; } = [];

    internal List<(string RoleKey, ICacheProviderOptionsExtension Extension)> TierExtensions { get; } = [];

    internal List<(string Name, ICacheProviderOptionsExtension Extension)> NamedExtensions { get; } = [];

    internal List<ICacheProviderOptionsExtension> CrossCuttingExtensions { get; } = [];

    /// <summary>Queues the default (unkeyed) cache provider contribution under its role key.</summary>
    /// <param name="roleKey">The reserved role key the default provider also aliases.</param>
    /// <param name="extension">The provider's deferred service contribution.</param>
    public void RegisterDefaultProvider(string roleKey, ICacheProviderOptionsExtension extension)
    {
        Argument.IsNotNullOrWhiteSpace(roleKey);
        Argument.IsNotNull(extension);

        DefaultExtensions.Add((roleKey, extension));
    }

    /// <summary>Queues a role-keyed tier provider contribution (e.g. the memory/remote tiers of a default hybrid).</summary>
    /// <param name="roleKey">One of the reserved role keys.</param>
    /// <param name="extension">The provider's deferred service contribution.</param>
    public void RegisterTierProvider(string roleKey, ICacheProviderOptionsExtension extension)
    {
        Argument.IsNotNullOrWhiteSpace(roleKey);
        Argument.IsNotNull(extension);

        if (!CacheConstants.IsReservedProviderKey(roleKey))
        {
            throw new ArgumentException(
                $"The tier role key '{roleKey}' is not one of the reserved role keys "
                    + $"('{CacheConstants.MemoryCacheProvider}', '{CacheConstants.RemoteCacheProvider}', "
                    + $"'{CacheConstants.HybridCacheProvider}').",
                nameof(roleKey)
            );
        }

        if (TierExtensions.Any(tier => string.Equals(tier.RoleKey, roleKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"A tier provider is already registered for the role key '{roleKey}'.");
        }

        TierExtensions.Add((roleKey, extension));
    }

    /// <summary>Queues a cross-cutting contribution (e.g. the distributed factory lock) applied after all providers.</summary>
    /// <param name="extension">The deferred service contribution.</param>
    public void RegisterCrossCuttingExtension(ICacheProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        CrossCuttingExtensions.Add(extension);
    }

    /// <summary>
    /// Adds an independently-configured named cache instance, resolvable as a keyed <see cref="ICache"/>
    /// service or through <see cref="ICacheProvider"/>. Named instances never touch the default (unkeyed)
    /// <see cref="ICache"/> nor the reserved role keys.
    /// </summary>
    /// <param name="name">The cache instance name. Must be non-empty and not a reserved role key.</param>
    /// <param name="configure">Configuration action that selects exactly one provider for the instance.</param>
    /// <returns>The builder for chaining.</returns>
    public HeadlessCachingSetupBuilder AddNamed(string name, Action<HeadlessCacheInstanceBuilder> configure)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(configure);

        if (CacheConstants.IsReservedProviderKey(name))
        {
            throw new ArgumentException(
                $"The cache name '{name}' is reserved for the role-keyed registrations "
                    + $"('{CacheConstants.MemoryCacheProvider}', '{CacheConstants.RemoteCacheProvider}', "
                    + $"'{CacheConstants.HybridCacheProvider}'). Pick a different instance name.",
                nameof(name)
            );
        }

        if (!_instanceNames.Add(name))
        {
            throw new InvalidOperationException($"A named cache instance '{name}' is already configured.");
        }

        var instance = new HeadlessCacheInstanceBuilder(name);
        configure(instance);

        if (instance.RegistrationCount == 0)
        {
            throw new InvalidOperationException(
                $"Named cache instance '{name}' requires exactly one provider. "
                    + "Call one of `UseInMemory`, `UseRedis`, or `UseHybrid`."
            );
        }

        if (instance.RegistrationCount > 1)
        {
            throw new InvalidOperationException(
                $"Multiple providers were configured for named cache instance '{name}'."
            );
        }

        NamedExtensions.Add((name, instance.Extension!));

        return this;
    }
}
