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

        #endregion

        #region Distributed Lock - Core Wiring

        private IServiceCollection _AddDistributedLockCore<TStorage>()
            where TStorage : class, IDistributedLockStorage
        {
            services.TryAddSingleton<TStorage>();

            return services._AddDistributedLockCore(static provider => provider.GetRequiredService<TStorage>());
        }

        private IServiceCollection _AddDistributedLockCore(
            Func<IServiceProvider, IDistributedLockStorage> storageFactory
        )
        {
            services.AddSingletonOptionValue<DistributedLockOptions>();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());

            services.AddSingleton<IDistributedLockProvider>(provider => new DistributedLockProvider(
                storageFactory(provider),
                provider.GetService<IOutboxPublisher>(),
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
    }
}
