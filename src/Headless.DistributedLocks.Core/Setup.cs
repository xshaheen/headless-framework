// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

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

            // TryAddSingleton on the concrete + the public interface keeps repeated
            // AddDistributedLock(...) calls idempotent (matching the ICanReceiveLockReleased
            // registration below). Two AddSingleton calls would accumulate descriptors and
            // register two distinct lambdas resolving against the same concrete type.
            services.TryAddSingleton<DistributedLock>(provider => new DistributedLock(
                storageFactory(provider),
                provider.GetService<IOutboxBus>(),
                provider.GetRequiredService<DistributedLockOptions>(),
                provider.GetRequiredService<ILongIdGenerator>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<DistributedLock>>()
            ));

            services.TryAddSingleton<IDistributedLock>(sp => sp.GetRequiredService<DistributedLock>());

            // Register ICanReceiveLockReleased pointing at the same concrete instance so that a
            // decorator wrapped around IDistributedLock does not break the lock-release
            // wake-up signal (the consumer always receives the real DistributedLock).
            // TryAddEnumerable keeps repeated AddDistributedLock(...) calls idempotent — the same
            // implementation type is not added twice — and LockReleasedConsumer fans out over the
            // collected IEnumerable<ICanReceiveLockReleased> so mutex and semaphore providers share
            // one decoupled wake-up seam.
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<ICanReceiveLockReleased, DistributedLock>(static sp =>
                    sp.GetRequiredService<DistributedLock>()
                )
            );

            // Auto-register the shared lock-released consumer. Order-independent: the registration
            // is drained into the messaging consumer registry by AddHeadlessMessaging regardless of
            // whether messaging was added before or after AddDistributedLock(...), so there is no
            // registration-order footgun and no opt-in step. When messaging is never added, the
            // emitted descriptors are inert.
            DistributedLockConsumerRegistration.TryAddLockReleasedConsumer(services);

            return services;
        }

        #endregion
    }
}
