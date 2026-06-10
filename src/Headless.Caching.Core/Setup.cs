// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Caching;

[PublicAPI]
public static class SetupCacheProvider
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="ICacheProvider"/> backed by the container's keyed <see cref="ICache"/>
        /// registrations. Called by every cache provider setup, so the provider is available whenever any
        /// cache is registered. Safe to call multiple times.
        /// </summary>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddCacheProvider()
        {
            services.TryAddSingleton<ICacheProvider>(provider => new KeyedServiceCacheProvider(provider));

            return services;
        }
    }
}
