// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

[PublicAPI]
public static class AddResourceLockExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddResourceLock<TStorage>(
            Action<ResourceLockOptions, IServiceProvider>? optionSetupAction = null
        )
            where TStorage : class, IResourceLockStorage
        {
            services.AddSingleton<IResourceLockStorage, TStorage>();

            return services.AddResourceLock(
                provider => provider.GetRequiredService<IResourceLockStorage>(),
                provider => provider.GetRequiredService<IOutboxPublisher>(),
                optionSetupAction
            );
        }

        public IServiceCollection AddResourceLock(
            Func<IServiceProvider, IResourceLockStorage> storageSetupAction,
            Func<IServiceProvider, IOutboxPublisher> publisherSetupAction,
            Action<ResourceLockOptions, IServiceProvider>? optionSetupAction = null
        )
        {
            services.Configure<ResourceLockOptions, ResourceLockOptionsValidator>(optionSetupAction);
            services.AddSingletonOptionValue<ResourceLockOptions>();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator(1));

            services.AddSingleton<IResourceLockProvider>(provider => new ResourceLockProvider(
                storageSetupAction(provider),
                publisherSetupAction(provider),
                provider.GetRequiredService<ResourceLockOptions>(),
                provider.GetRequiredService<ILongIdGenerator>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<ResourceLockProvider>>()
            ));

            services
                .AddConsumer<ResourceLockProvider.LockReleasedConsumer, ResourceLockReleased>(
                    "framework.locks.released"
                )
                .WithConcurrency(1);

            return services;
        }

        public IServiceCollection AddThrottlingResourceLock(
            ThrottlingResourceLockOptions options,
            Func<IServiceProvider, IThrottlingResourceLockStorage> setupAction
        )
        {
            services.AddLogging();
            services.TryAddSingleton(TimeProvider.System);

            services.AddSingleton<IThrottlingResourceLockProvider>(provider => new ThrottlingResourceLockProvider(
                setupAction(provider),
                options,
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<ThrottlingResourceLockProvider>>()
            ));

            return services;
        }

        public IServiceCollection AddKeyedThrottlingResourceLock(
            string key,
            ThrottlingResourceLockOptions options,
            Func<IServiceProvider, IThrottlingResourceLockStorage> setupAction
        )
        {
            services.AddLogging();
            services.TryAddSingleton(TimeProvider.System);

            services.AddKeyedSingleton<IThrottlingResourceLockProvider>(
                key,
                provider => new ThrottlingResourceLockProvider(
                    setupAction(provider),
                    options,
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<ILogger<ThrottlingResourceLockProvider>>()
                )
            );

            return services;
        }
    }
}
