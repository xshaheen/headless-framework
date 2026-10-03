// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;
using Headless.Idempotency.Caching;
using Headless.UnitOfWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Idempotency;

/// <summary>Chooses the application's <see cref="ICache" /> as the idempotency provider.</summary>
[PublicAPI]
public static class SetupIdempotencyCaching
{
    extension(HeadlessIdempotencySetupBuilder setup)
    {
        /// <summary>
        /// Keeps idempotency records in the application's remote cache with the default
        /// <see cref="CacheIdempotencyOptions" />: autonomous calls only, shared by every replica that uses the same
        /// cache.
        /// </summary>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// Needs a caching provider registered through <c>AddHeadlessCaching</c>; a shared one (Redis) is what makes
        /// the records hold across replicas. Only <see cref="IIdempotentOperations" /> works: every
        /// <c>unit.Idempotency</c> call is refused, because a cache cannot commit or roll back with a unit of work.
        /// Records last only as long as the cache keeps them, and leases run on the application clock.
        /// </remarks>
        public HeadlessIdempotencySetupBuilder UseCache()
        {
            setup.RegisterExtension(new CacheIdempotencyOptionsExtension());

            return setup;
        }

        /// <summary>Keeps idempotency records in a cache, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks><inheritdoc cref="UseCache(HeadlessIdempotencySetupBuilder)" path="/remarks" /></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UseCache(Action<CacheIdempotencyOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new CacheIdempotencyOptionsExtension(configure: configure));

            return setup;
        }

        /// <summary>
        /// Keeps idempotency records in a cache, configured by <paramref name="configure" /> with access to the
        /// application services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks><inheritdoc cref="UseCache(HeadlessIdempotencySetupBuilder)" path="/remarks" /></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UseCache(Action<CacheIdempotencyOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new CacheIdempotencyOptionsExtension(configureWithServices: configure));

            return setup;
        }

        /// <summary>
        /// Keeps idempotency records in a cache, binding <see cref="CacheIdempotencyOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks><inheritdoc cref="UseCache(HeadlessIdempotencySetupBuilder)" path="/remarks" /></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UseCache(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new CacheIdempotencyOptionsExtension(configuration: configuration));

            return setup;
        }
    }
}
