// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

[PublicAPI]
public static class AddDistributedSemaphoreExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDistributedSemaphore<TStorage>(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
            where TStorage : class, IDistributedSemaphoreStorage
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(optionSetupAction);

            return services._AddDistributedSemaphoreCore<TStorage>();
        }

        public IServiceCollection AddDistributedSemaphore<TStorage>(Action<DistributedLockOptions> optionSetupAction)
            where TStorage : class, IDistributedSemaphoreStorage
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(optionSetupAction);

            return services._AddDistributedSemaphoreCore<TStorage>();
        }

        public IServiceCollection AddDistributedSemaphore<TStorage>(IConfiguration config)
            where TStorage : class, IDistributedSemaphoreStorage
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(config);

            return services._AddDistributedSemaphoreCore<TStorage>();
        }

        private IServiceCollection _AddDistributedSemaphoreCore<TStorage>()
            where TStorage : class, IDistributedSemaphoreStorage
        {
            services.TryAddSingleton<TStorage>();
            services.AddSingletonOptionValue<DistributedLockOptions>();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());

            services.TryAddSingleton<DistributedSemaphoreProvider>(provider =>
                new DistributedSemaphoreProvider(
                    provider.GetRequiredService<TStorage>(),
                    provider.GetService<IOutboxBus>(),
                    provider.GetRequiredService<DistributedLockOptions>(),
                    provider.GetRequiredService<ILongIdGenerator>(),
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<ILogger<DistributedSemaphoreProvider>>()
                )
            );

            services.TryAddSingleton<IDistributedSemaphoreProvider>(sp =>
                sp.GetRequiredService<DistributedSemaphoreProvider>()
            );
            DistributedLockConsumerRegistration.TryAddLockReleasedConsumer(services);

            return services;
        }
    }
}
