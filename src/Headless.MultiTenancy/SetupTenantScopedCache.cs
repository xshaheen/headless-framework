// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.DependencyInjection;

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
        /// <see cref="MissingTenantContextException"/> instead of reading or writing a shared entry; cache values
        /// every tenant shares through the unscoped <see cref="ICache"/>. A tenant id containing <c>:</c> is refused
        /// with <see cref="InvalidOperationException"/>,
        /// because <c>t:a:b:x</c> could not tell tenant <c>a</c> with key <c>b:x</c> from tenant <c>a:b</c> with
        /// key <c>x</c>.
        /// </para>
        /// <para>
        /// <c>RemoveByPrefixAsync("")</c> evicts every entry of the current tenant in the underlying cache, including
        /// entries other tenant-scoped types and the permission grant cache wrote under the same scope. Tag
        /// invalidation, <c>ClearAsync</c>, and <c>FlushAsync</c> are not tenant-isolated; see
        /// <see cref="ScopedCache{T}"/>.
        /// </para>
        /// <para>
        /// Calling this more than once for the same <typeparamref name="T"/> is a no-op. The host must register an
        /// <see cref="ICurrentTenant"/>, as the HTTP tenancy seam and <c>AddHeadlessDbContextServices()</c> do;
        /// startup fails otherwise.
        /// </para>
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

            // A closed-generic registration wins over a provider's open-generic ICache<> whatever the order, so the
            // scoped wrapper cannot be shadowed by AddHeadlessCaching running later.
            services.AddSingleton<ICache<T>>(provider =>
            {
                var currentTenant = provider.GetRequiredService<ICurrentTenant>();

                return new ScopedCache<T>(
                    provider.GetRequiredService<ICache>(),
                    () => TenantCacheScope.Resolve(currentTenant)
                );
            });

            services.RequireRegisteredService<ICache>(
                requiredBy: $"Tenant-scoped cache ICache<{typeof(T).Name}>",
                remedy: "Call AddHeadlessCaching(...) with a provider (UseInMemory / UseRedis / UseHybrid)."
            );

            // Headless.MultiTenancy carries no ICurrentTenant implementation, and a fallback whose Id is always null
            // would only turn a wiring mistake into a MissingTenantContextException on every call.
            services.RequireRegisteredService<ICurrentTenant>(
                requiredBy: $"Tenant-scoped cache ICache<{typeof(T).Name}>",
                remedy: "Register a tenant source: AddHeadlessTenancy(t => t.Http(http => http.ResolveFromClaims())), "
                    + "AddHeadlessDbContextServices(), or your own ICurrentTenant implementation."
            );

            return services;
        }
    }
}
