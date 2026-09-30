// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.MultiTenancy;

/// <summary>Registers application caches whose keys are isolated per tenant.</summary>
[PublicAPI]
public static class SetupTenantScopedCache
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="ICache{T}"/> as a <see cref="ScopedCache{T}"/> over the default <see cref="ICache"/>
        /// whose keys carry the ambient tenant: key <c>user:1</c> is stored as <c>t:{tenantId}:user:1</c>.
        /// </summary>
        /// <typeparam name="T">The cached value type.</typeparam>
        /// <returns>The service collection for chaining.</returns>
        /// <exception cref="InvalidOperationException">
        /// Another closed <see cref="ICache{T}"/> registration for <typeparamref name="T"/> already exists, so this
        /// call would silently lose to it or replace it.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The scope is read on every operation. With no ambient tenant the operation throws
        /// <see cref="MissingTenantContextException"/> instead of reading or writing a shared entry, unless
        /// <see cref="ITenantStorageScopeBypass"/> is active, in which case it addresses the shared host scope
        /// <c>t::{key}</c>. A tenant id containing <c>:</c> is refused with <see cref="InvalidOperationException"/>,
        /// because <c>t:a:b:x</c> could not tell tenant <c>a</c> with key <c>b:x</c> from tenant <c>a:b</c> with
        /// key <c>x</c>.
        /// </para>
        /// <para>
        /// <c>RemoveByPrefixAsync("")</c> evicts every entry of the current tenant in the underlying cache, including
        /// entries other tenant-scoped types and the permission grant cache wrote under the same scope. Tag
        /// invalidation, <c>ClearAsync</c>, and <c>FlushAsync</c> are not tenant-isolated; see
        /// <see cref="ScopedCache{T}"/>.
        /// </para>
        /// <para>Calling this more than once for the same <typeparamref name="T"/> is a no-op.</para>
        /// </remarks>
        public IServiceCollection AddTenantScopedCache<T>()
        {
            if (services.Any(static d => d.ServiceType == typeof(TenantScopedCacheRegistration<T>)))
            {
                return services;
            }

            if (services.Any(static d => !d.IsKeyedService && d.ServiceType == typeof(ICache<T>)))
            {
                throw new InvalidOperationException(
                    $"ICache<{typeof(T).Name}> is already registered, so AddTenantScopedCache<{typeof(T).Name}>() "
                        + "would either lose to that registration and leave the cache shared across tenants, or "
                        + "replace a registration its owner relies on. Remove the existing registration or cache a "
                        + "dedicated type."
                );
            }

            services.AddSingleton<TenantScopedCacheRegistration<T>>();

            // The AsyncLocal-backed defaults, replaced by any real tenancy registration; the same fallback
            // Permissions, Messaging, and Jobs register.
            services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
            services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();
            services.TryAddSingleton<ITenantStorageScopeBypass>(TenantStorageScopeBypass.Instance);

            // A closed-generic registration wins over a provider's open-generic ICache<> whatever the order, so the
            // scoped wrapper cannot be shadowed by AddHeadlessCaching running later.
            services.AddSingleton<ICache<T>>(provider =>
            {
                var currentTenant = provider.GetRequiredService<ICurrentTenant>();
                var bypass = provider.GetRequiredService<ITenantStorageScopeBypass>();

                return new ScopedCache<T>(
                    provider.GetRequiredService<ICache>(),
                    () => TenantCacheScope.Resolve(currentTenant, bypass)
                );
            });

            services.RequireRegisteredService<ICache>(
                requiredBy: $"Tenant-scoped cache ICache<{typeof(T).Name}>",
                remedy: "Call AddHeadlessCaching(...) with a provider (UseInMemory / UseRedis / UseHybrid)."
            );

            return services;
        }
    }
}

/// <summary>Resolves the <see cref="ScopedCache{T}"/> scope for a tenant-scoped cache.</summary>
internal static class TenantCacheScope
{
    /// <summary>The scope a bypassed operation uses; the same shape the permission grant cache uses with no tenant.</summary>
    public const string HostScope = "t:";

    public static string Resolve(ICurrentTenant currentTenant, ITenantStorageScopeBypass bypass)
    {
        if (bypass.IsActive)
        {
            return HostScope;
        }

        var tenantId = currentTenant.Id;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new MissingTenantContextException(
                "A tenant-scoped cache was used with no ambient tenant. Wrap the call in "
                    + "ICurrentTenant.Change(tenantId), or use ITenantStorageScopeBypass.BeginBypass() for an "
                    + "intentional host-level entry."
            );
        }

        if (tenantId.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A tenant-scoped cache cannot scope a tenant id that contains ':', because the resulting key would "
                    + "be indistinguishable from another tenant's key."
            );
        }

        return HostScope + tenantId;
    }
}

/// <summary>Marks that <c>AddTenantScopedCache&lt;T&gt;()</c> already registered <see cref="ICache{T}"/>.</summary>
internal sealed class TenantScopedCacheRegistration<T>;
