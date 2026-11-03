// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Caching;

/// <summary>
/// Chooses the cache a feature keeps shared state in when the state lives only in the shared tier: compare-and-swap
/// records and atomic counters that are written and read through one call and never read back through a local tier.
/// </summary>
[PublicAPI]
public static class HeadlessRemoteCacheServiceProviderExtensions
{
    extension(IServiceProvider services)
    {
        /// <summary>
        /// Returns the registered <see cref="IRemoteCache"/> when there is one, otherwise the default
        /// <see cref="ICache"/>.
        /// </summary>
        /// <remarks>
        /// A hybrid default <see cref="ICache"/> answers reads from a local tier that can lag behind the shared one,
        /// and every write through it also writes the local tier and broadcasts an invalidation to every instance.
        /// State that only ever round-trips through the shared tier gains nothing from either, so this skips the
        /// hybrid when its remote tier is registered on its own (<c>setup.AddRedisTier(...)</c> or
        /// <c>setup.UseRedis(...)</c>). A host whose default is in-memory and registers no remote tier gets the
        /// in-memory cache.
        /// </remarks>
        /// <returns>The remote tier, or the default cache.</returns>
        /// <exception cref="InvalidOperationException">Neither an <see cref="IRemoteCache"/> nor an <see cref="ICache"/> is registered.</exception>
        public ICache GetRemoteOrDefaultCache()
        {
            Argument.IsNotNull(services);

            return services.GetService(typeof(IRemoteCache)) as ICache
                ?? services.GetService(typeof(ICache)) as ICache
                ?? throw new InvalidOperationException(
                    "No ICache is registered. Call AddHeadlessCaching(...) with a shared provider (UseRedis or "
                        + "UseHybrid)."
                );
        }
    }
}
