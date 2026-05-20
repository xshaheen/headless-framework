// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.RateLimiting;

[PublicAPI]
public static class AddRateLimitingExtensions
{
    extension(IServiceCollection services)
    {
        #region Rate Limiter - Typed Storage

        public IServiceCollection AddRateLimiter<TStorage>(
            Action<SlidingWindowRateLimiterOptions, IServiceProvider> optionSetupAction
        )
            where TStorage : class, IDistributedRateLimiterStorage
        {
            services.Configure<SlidingWindowRateLimiterOptions, SlidingWindowRateLimiterOptionsValidator>(
                optionSetupAction
            );

            return services._AddRateLimiterCore<TStorage>();
        }

        public IServiceCollection AddRateLimiter<TStorage>(Action<SlidingWindowRateLimiterOptions> optionSetupAction)
            where TStorage : class, IDistributedRateLimiterStorage
        {
            services.Configure<SlidingWindowRateLimiterOptions, SlidingWindowRateLimiterOptionsValidator>(
                optionSetupAction
            );

            return services._AddRateLimiterCore<TStorage>();
        }

        public IServiceCollection AddRateLimiter<TStorage>(IConfiguration config)
            where TStorage : class, IDistributedRateLimiterStorage
        {
            services.Configure<SlidingWindowRateLimiterOptions, SlidingWindowRateLimiterOptionsValidator>(config);

            return services._AddRateLimiterCore<TStorage>();
        }

        #endregion

        #region Rate Limiter - Custom Storage Factory

        public IServiceCollection AddRateLimiter(
            Func<IServiceProvider, IDistributedRateLimiterStorage> storageFactory,
            Action<SlidingWindowRateLimiterOptions, IServiceProvider> optionSetupAction
        )
        {
            services.Configure<SlidingWindowRateLimiterOptions, SlidingWindowRateLimiterOptionsValidator>(
                optionSetupAction
            );

            return services._AddRateLimiterCore(storageFactory);
        }

        public IServiceCollection AddRateLimiter(
            Func<IServiceProvider, IDistributedRateLimiterStorage> storageFactory,
            Action<SlidingWindowRateLimiterOptions> optionSetupAction
        )
        {
            services.Configure<SlidingWindowRateLimiterOptions, SlidingWindowRateLimiterOptionsValidator>(
                optionSetupAction
            );

            return services._AddRateLimiterCore(storageFactory);
        }

        public IServiceCollection AddRateLimiter(
            Func<IServiceProvider, IDistributedRateLimiterStorage> storageFactory,
            IConfiguration config
        )
        {
            services.Configure<SlidingWindowRateLimiterOptions, SlidingWindowRateLimiterOptionsValidator>(config);

            return services._AddRateLimiterCore(storageFactory);
        }

        #endregion

        #region Rate Limiter - Core Wiring

        private IServiceCollection _AddRateLimiterCore<TStorage>()
            where TStorage : class, IDistributedRateLimiterStorage
        {
            services.TryAddSingleton<TStorage>();

            return services._AddRateLimiterCore(static provider => provider.GetRequiredService<TStorage>());
        }

        private IServiceCollection _AddRateLimiterCore(
            Func<IServiceProvider, IDistributedRateLimiterStorage> storageFactory
        )
        {
            services.AddLogging();
            services.AddSingletonOptionValue<SlidingWindowRateLimiterOptions>();
            services.TryAddSingleton(TimeProvider.System);

            services.AddSingleton<IDistributedRateLimiter>(provider => new SlidingWindowDistributedRateLimiter(
                storageFactory(provider),
                provider.GetRequiredService<SlidingWindowRateLimiterOptions>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<SlidingWindowDistributedRateLimiter>>()
            ));

            return services;
        }

        #endregion

        #region Keyed Rate Limiter - Typed Storage

        public IServiceCollection AddKeyedRateLimiter<TStorage>(
            string key,
            Action<SlidingWindowRateLimiterOptions, IServiceProvider> optionSetupAction
        )
            where TStorage : class, IDistributedRateLimiterStorage
        {
            Argument.IsNotNullOrEmpty(key);

            services.Configure<SlidingWindowRateLimiterOptions, SlidingWindowRateLimiterOptionsValidator>(
                optionSetupAction,
                name: key
            );

            return services._AddKeyedRateLimiterCore<TStorage>(key);
        }

        public IServiceCollection AddKeyedRateLimiter<TStorage>(
            string key,
            Action<SlidingWindowRateLimiterOptions> optionSetupAction
        )
            where TStorage : class, IDistributedRateLimiterStorage
        {
            Argument.IsNotNullOrEmpty(key);

            services.Configure<SlidingWindowRateLimiterOptions, SlidingWindowRateLimiterOptionsValidator>(
                optionSetupAction,
                name: key
            );

            return services._AddKeyedRateLimiterCore<TStorage>(key);
        }

        public IServiceCollection AddKeyedRateLimiter<TStorage>(string key, IConfiguration config)
            where TStorage : class, IDistributedRateLimiterStorage
        {
            Argument.IsNotNullOrEmpty(key);

            services.Configure<SlidingWindowRateLimiterOptions, SlidingWindowRateLimiterOptionsValidator>(
                config,
                name: key
            );

            return services._AddKeyedRateLimiterCore<TStorage>(key);
        }

        #endregion

        #region Keyed Rate Limiter - Custom Storage Factory

        public IServiceCollection AddKeyedRateLimiter(
            string key,
            Func<IServiceProvider, IDistributedRateLimiterStorage> storageFactory,
            Action<SlidingWindowRateLimiterOptions, IServiceProvider> optionSetupAction
        )
        {
            Argument.IsNotNullOrEmpty(key);

            services.Configure<SlidingWindowRateLimiterOptions, SlidingWindowRateLimiterOptionsValidator>(
                optionSetupAction,
                name: key
            );

            return services._AddKeyedRateLimiterCore(key, storageFactory);
        }

        public IServiceCollection AddKeyedRateLimiter(
            string key,
            Func<IServiceProvider, IDistributedRateLimiterStorage> storageFactory,
            Action<SlidingWindowRateLimiterOptions> optionSetupAction
        )
        {
            Argument.IsNotNullOrEmpty(key);

            services.Configure<SlidingWindowRateLimiterOptions, SlidingWindowRateLimiterOptionsValidator>(
                optionSetupAction,
                name: key
            );

            return services._AddKeyedRateLimiterCore(key, storageFactory);
        }

        public IServiceCollection AddKeyedRateLimiter(
            string key,
            Func<IServiceProvider, IDistributedRateLimiterStorage> storageFactory,
            IConfiguration config
        )
        {
            Argument.IsNotNullOrEmpty(key);

            services.Configure<SlidingWindowRateLimiterOptions, SlidingWindowRateLimiterOptionsValidator>(
                config,
                name: key
            );

            return services._AddKeyedRateLimiterCore(key, storageFactory);
        }

        #endregion

        #region Keyed Rate Limiter - Core Wiring

        private IServiceCollection _AddKeyedRateLimiterCore<TStorage>(string key)
            where TStorage : class, IDistributedRateLimiterStorage
        {
            services.TryAddSingleton<TStorage>();

            return services._AddKeyedRateLimiterCore(key, static provider => provider.GetRequiredService<TStorage>());
        }

        private IServiceCollection _AddKeyedRateLimiterCore(
            string key,
            Func<IServiceProvider, IDistributedRateLimiterStorage> storageFactory
        )
        {
            services.AddLogging();
            services.TryAddSingleton(TimeProvider.System);

            services.AddKeyedSingleton<IDistributedRateLimiter>(
                key,
                (provider, _) =>
                    new SlidingWindowDistributedRateLimiter(
                        storageFactory(provider),
                        provider.GetRequiredService<IOptionsMonitor<SlidingWindowRateLimiterOptions>>().Get(key),
                        provider.GetRequiredService<TimeProvider>(),
                        provider.GetRequiredService<ILogger<SlidingWindowDistributedRateLimiter>>()
                    )
            );

            return services;
        }

        #endregion
    }
}
