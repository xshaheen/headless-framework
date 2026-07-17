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

    internal IReadOnlySet<string> InstanceNames => _instanceNames;

    /// <summary>
    /// Whether caching spans may carry the raw cache key on the <c>headless.cache.key</c> attribute. Default
    /// <see langword="false"/>: cache keys routinely carry tenant/user identifiers and PII cannot be un-leaked
    /// from a trace backend. The key is never a metric dimension regardless of this flag. Applies to every cache
    /// instance registered by this <c>AddHeadlessCaching</c> call.
    /// </summary>
    public bool IncludeKeyInTraces { get; set; }

    internal List<(string RoleKey, Action<IServiceCollection> Action)> DefaultExtensions { get; } = [];

    internal List<(string RoleKey, Action<IServiceCollection> Action)> TierExtensions { get; } = [];

    internal List<(string Name, Action<IServiceCollection> Action)> NamedExtensions { get; } = [];

    internal List<Action<IServiceCollection>> CrossCuttingExtensions { get; } = [];

    /// <summary>Queues the default (unkeyed) cache provider contribution under its role key.</summary>
    /// <param name="roleKey">The reserved role key the default provider also aliases.</param>
    /// <param name="action">The provider's deferred service registration action.</param>
    public void RegisterDefaultProvider(string roleKey, Action<IServiceCollection> action)
    {
        Argument.IsNotNullOrWhiteSpace(roleKey);
        Argument.IsNotNull(action);

        DefaultExtensions.Add((roleKey, action));
    }

    /// <summary>Queues a role-keyed tier provider contribution (e.g. the memory/remote tiers of a default hybrid).</summary>
    /// <param name="roleKey">One of the reserved role keys.</param>
    /// <param name="action">The provider's deferred service registration action.</param>
    public void RegisterTierProvider(string roleKey, Action<IServiceCollection> action)
    {
        Argument.IsNotNullOrWhiteSpace(roleKey);
        Argument.IsNotNull(action);

        Argument.IsOneOf(
            roleKey,
            [
                CacheConstants.MemoryCacheProvider,
                CacheConstants.RemoteCacheProvider,
                CacheConstants.HybridCacheProvider,
            ],
            message: $"The tier role key '{roleKey}' is not one of the reserved role keys "
                + $"('{CacheConstants.MemoryCacheProvider}', '{CacheConstants.RemoteCacheProvider}', "
                + $"'{CacheConstants.HybridCacheProvider}')."
        );

        if (TierExtensions.Exists(tier => string.Equals(tier.RoleKey, roleKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"A tier provider is already registered for the role key '{roleKey}'.");
        }

        TierExtensions.Add((roleKey, action));
    }

    /// <summary>Queues a cross-cutting contribution (e.g. the distributed factory lock) applied after all providers.</summary>
    /// <param name="action">The deferred service registration action.</param>
    public void RegisterCrossCuttingExtension(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        CrossCuttingExtensions.Add(action);
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

        Argument.IsTrue(
            !CacheConstants.IsReservedProviderKey(name),
            $"The cache name '{name}' is reserved for the role-keyed registrations "
                + $"('{CacheConstants.MemoryCacheProvider}', '{CacheConstants.RemoteCacheProvider}', "
                + $"'{CacheConstants.HybridCacheProvider}', and the 'Headless.Caching:' "
                + "namespace). Pick a different instance name.",
            nameof(name)
        );

        if (!_instanceNames.Add(name))
        {
            throw new InvalidOperationException($"A named cache instance '{name}' is already configured.");
        }

        var instance = new HeadlessCacheInstanceBuilder(name);
        configure(instance);

        if (instance.Action is null)
        {
            throw new InvalidOperationException(
                $"Named cache instance '{name}' requires exactly one provider. "
                    + "Call one of `UseInMemory`, `UseRedis`, or `UseHybrid`."
            );
        }

        NamedExtensions.Add((name, instance.Action));

        return this;
    }
}
