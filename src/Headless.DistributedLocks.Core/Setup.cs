// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

            // TryAddSingleton on the concrete + the public interface keeps repeated
            // AddDistributedLock(...) calls idempotent (matching the ICanReceiveLockReleased
            // registration below). Two AddSingleton calls would accumulate descriptors and
            // register two distinct lambdas resolving against the same concrete type.
            services.TryAddSingleton<DistributedLockProvider>(provider => new DistributedLockProvider(
                storageFactory(provider),
                provider.GetService<IOutboxBus>(),
                provider.GetRequiredService<DistributedLockOptions>(),
                provider.GetRequiredService<ILongIdGenerator>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<DistributedLockProvider>>()
            ));

            services.TryAddSingleton<IDistributedLockProvider>(sp => sp.GetRequiredService<DistributedLockProvider>());

            // Register ICanReceiveLockReleased pointing at the same concrete instance so that a
            // decorator wrapped around IDistributedLockProvider does not break the lock-release
            // wake-up signal (the consumer always receives the real DistributedLockProvider).
            services.TryAddSingleton<ICanReceiveLockReleased>(sp => sp.GetRequiredService<DistributedLockProvider>());

            // Startup-time hook that warns when IOutboxBus is registered AFTER
            // AddDistributedLock(...). The consumer registration block below only runs at
            // registration time; a later AddMessages(...) call silently skips push wake-ups
            // and the existing LogOutboxBusAbsent warning would NOT fire (outbox bus is
            // non-null at runtime). See DistributedLockMessagingValidator for the rationale.
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IValidateOptions<DistributedLockOptions>,
                    DistributedLockMessagingValidator
                >()
            );

            // Only register the lock-released consumer when an IOutboxBus is available; the
            // consumer's only job is to wake waiters when DistributedLockReleased messages arrive,
            // which themselves only get published via the outbox path. Without IOutboxBus no
            // such messages ever flow, so the consumer registration is dead weight.
            if (services.Any(d => d.ServiceType == typeof(IOutboxBus)))
            {
                services
                    .AddBusConsumer<DistributedLockProvider.LockReleasedConsumer, DistributedLockReleased>(
                        "headless.locks.released"
                    )
                    .Concurrency(1);
            }

            return services;
        }

        #endregion
    }
}
