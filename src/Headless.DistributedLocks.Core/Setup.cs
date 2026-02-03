// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

[PublicAPI]
public static class AddDistributedLockExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDistributedLock<TStorage>(
            Action<DistributedLockOptions, IServiceProvider>? optionSetupAction = null
        )
            where TStorage : class, IDistributedLockStorage
        {
            services.AddSingleton<IDistributedLockStorage, TStorage>();

            return services.AddDistributedLock(
                provider => provider.GetRequiredService<IDistributedLockStorage>(),
                provider => provider.GetRequiredService<IOutboxPublisher>(),
                optionSetupAction
            );
        }

        public IServiceCollection AddDistributedLock(
            Func<IServiceProvider, IDistributedLockStorage> storageSetupAction,
            Func<IServiceProvider, IOutboxPublisher> publisherSetupAction,
            Action<DistributedLockOptions, IServiceProvider>? optionSetupAction = null
        )
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(optionSetupAction);
            services.AddSingletonOptionValue<DistributedLockOptions>();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator(1));

            services.AddSingleton<IDistributedLockProvider>(provider => new DistributedLockProvider(
                storageSetupAction(provider),
                publisherSetupAction(provider),
                provider.GetRequiredService<DistributedLockOptions>(),
                provider.GetRequiredService<ILongIdGenerator>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<DistributedLockProvider>>()
            ));

            services
                .AddConsumer<DistributedLockProvider.LockReleasedConsumer, DistributedLockReleased>(
                    "headless.locks.released"
                )
                .WithConcurrency(1);

            return services;
        }

        public IServiceCollection AddThrottlingDistributedLock(
            ThrottlingDistributedLockOptions options,
            Func<IServiceProvider, IThrottlingDistributedLockStorage> setupAction
        )
        {
            services.AddLogging();
            services.TryAddSingleton(TimeProvider.System);

            services.AddSingleton<IThrottlingDistributedLockProvider>(provider => new ThrottlingDistributedLockProvider(
                setupAction(provider),
                options,
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<ThrottlingDistributedLockProvider>>()
            ));

            return services;
        }

        public IServiceCollection AddKeyedThrottlingDistributedLock(
            string key,
            ThrottlingDistributedLockOptions options,
            Func<IServiceProvider, IThrottlingDistributedLockStorage> setupAction
        )
        {
            services.AddLogging();
            services.TryAddSingleton(TimeProvider.System);

            services.AddKeyedSingleton<IThrottlingDistributedLockProvider>(
                key,
                provider => new ThrottlingDistributedLockProvider(
                    setupAction(provider),
                    options,
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<ILogger<ThrottlingDistributedLockProvider>>()
                )
            );

            return services;
        }
    }
}
