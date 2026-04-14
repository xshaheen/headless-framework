// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

[PublicAPI]
public static class AddDistributedLockExtensions
{
    extension(IServiceCollection services)
    {
        #region Distributed Lock - Typed Storage

        public IServiceCollection AddDistributedLock<TStorage>(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
            where TStorage : class, IDistributedLockStorage
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(optionSetupAction);

            return services._AddDistributedLockCore<TStorage>();
        }

        public IServiceCollection AddDistributedLock<TStorage>(Action<DistributedLockOptions> optionSetupAction)
            where TStorage : class, IDistributedLockStorage
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(optionSetupAction);

            return services._AddDistributedLockCore<TStorage>();
        }

        public IServiceCollection AddDistributedLock<TStorage>(IConfiguration config)
            where TStorage : class, IDistributedLockStorage
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(config);

            return services._AddDistributedLockCore<TStorage>();
        }

        public IServiceCollection AddDistributedLock<TStorage>()
            where TStorage : class, IDistributedLockStorage
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(
                (Action<DistributedLockOptions>?)null
            );

            return services._AddDistributedLockCore<TStorage>();
        }

        #endregion

        #region Distributed Lock - Custom Storage Factory

        public IServiceCollection AddDistributedLock(
            Func<IServiceProvider, IDistributedLockStorage> storageFactory,
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(optionSetupAction);

            return services._AddDistributedLockCore(storageFactory);
        }

        public IServiceCollection AddDistributedLock(
            Func<IServiceProvider, IDistributedLockStorage> storageFactory,
            Action<DistributedLockOptions> optionSetupAction
        )
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(optionSetupAction);

            return services._AddDistributedLockCore(storageFactory);
        }

        public IServiceCollection AddDistributedLock(
            Func<IServiceProvider, IDistributedLockStorage> storageFactory,
            IConfiguration config
        )
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(config);

            return services._AddDistributedLockCore(storageFactory);
        }

        public IServiceCollection AddDistributedLock(Func<IServiceProvider, IDistributedLockStorage> storageFactory)
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(
                (Action<DistributedLockOptions>?)null
            );

            return services._AddDistributedLockCore(storageFactory);
        }

        #endregion

        #region Distributed Lock - Core Wiring

        private IServiceCollection _AddDistributedLockCore<TStorage>()
            where TStorage : class, IDistributedLockStorage
        {
            services.TryAddSingleton<IDistributedLockStorage, TStorage>();

            return services._AddDistributedLockCore(static provider =>
                provider.GetRequiredService<IDistributedLockStorage>()
            );
        }

        private IServiceCollection _AddDistributedLockCore(
            Func<IServiceProvider, IDistributedLockStorage> storageFactory
        )
        {
            services.AddSingletonOptionValue<DistributedLockOptions>();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator(1));

            services.AddSingleton<IDistributedLockProvider>(provider => new DistributedLockProvider(
                storageFactory(provider),
                provider.GetRequiredService<IOutboxPublisher>(),
                provider.GetRequiredService<DistributedLockOptions>(),
                provider.GetRequiredService<ILongIdGenerator>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<DistributedLockProvider>>()
            ));

            services
                .AddConsumer<DistributedLockProvider.LockReleasedConsumer, DistributedLockReleased>(
                    "headless.locks.released"
                )
                .Concurrency(1);

            return services;
        }

        #endregion

        #region Throttling Distributed Lock

        public IServiceCollection AddThrottlingDistributedLock(
            Func<IServiceProvider, IThrottlingDistributedLockStorage> storageFactory,
            Action<ThrottlingDistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            services.Configure<ThrottlingDistributedLockOptions, ThrottlingDistributedLockOptionsValidator>(
                optionSetupAction
            );

            return services._AddThrottlingDistributedLockCore(storageFactory);
        }

        public IServiceCollection AddThrottlingDistributedLock(
            Func<IServiceProvider, IThrottlingDistributedLockStorage> storageFactory,
            Action<ThrottlingDistributedLockOptions> optionSetupAction
        )
        {
            services.Configure<ThrottlingDistributedLockOptions, ThrottlingDistributedLockOptionsValidator>(
                optionSetupAction
            );

            return services._AddThrottlingDistributedLockCore(storageFactory);
        }

        public IServiceCollection AddThrottlingDistributedLock(
            Func<IServiceProvider, IThrottlingDistributedLockStorage> storageFactory,
            IConfiguration config
        )
        {
            services.Configure<ThrottlingDistributedLockOptions, ThrottlingDistributedLockOptionsValidator>(config);

            return services._AddThrottlingDistributedLockCore(storageFactory);
        }

        private IServiceCollection _AddThrottlingDistributedLockCore(
            Func<IServiceProvider, IThrottlingDistributedLockStorage> storageFactory
        )
        {
            services.AddLogging();
            services.AddSingletonOptionValue<ThrottlingDistributedLockOptions>();
            services.TryAddSingleton(TimeProvider.System);

            services.AddSingleton<IThrottlingDistributedLockProvider>(provider => new ThrottlingDistributedLockProvider(
                storageFactory(provider),
                provider.GetRequiredService<ThrottlingDistributedLockOptions>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<ThrottlingDistributedLockProvider>>()
            ));

            return services;
        }

        public IServiceCollection AddKeyedThrottlingDistributedLock(
            string key,
            ThrottlingDistributedLockOptions options,
            Func<IServiceProvider, IThrottlingDistributedLockStorage> storageFactory
        )
        {
            services.AddLogging();
            services.TryAddSingleton(TimeProvider.System);

            services.AddKeyedSingleton<IThrottlingDistributedLockProvider>(
                key,
                provider => new ThrottlingDistributedLockProvider(
                    storageFactory(provider),
                    options,
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<ILogger<ThrottlingDistributedLockProvider>>()
                )
            );

            return services;
        }

        #endregion
    }
}
