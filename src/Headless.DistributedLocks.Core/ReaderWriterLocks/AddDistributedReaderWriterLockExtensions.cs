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
public static class AddDistributedReaderWriterLockExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDistributedReaderWriterLock<TStorage>(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
            where TStorage : class, IDistributedReaderWriterLockStorage
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(optionSetupAction);

            return services._AddDistributedReaderWriterLockCore<TStorage>();
        }

        public IServiceCollection AddDistributedReaderWriterLock<TStorage>(
            Action<DistributedLockOptions> optionSetupAction
        )
            where TStorage : class, IDistributedReaderWriterLockStorage
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(optionSetupAction);

            return services._AddDistributedReaderWriterLockCore<TStorage>();
        }

        public IServiceCollection AddDistributedReaderWriterLock<TStorage>(IConfiguration config)
            where TStorage : class, IDistributedReaderWriterLockStorage
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(config);

            return services._AddDistributedReaderWriterLockCore<TStorage>();
        }

        private IServiceCollection _AddDistributedReaderWriterLockCore<TStorage>()
            where TStorage : class, IDistributedReaderWriterLockStorage
        {
            services.TryAddSingleton<TStorage>();
            services.AddSingletonOptionValue<DistributedLockOptions>();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());

            services.TryAddSingleton<DistributedReaderWriterLockProvider>(
                provider => new DistributedReaderWriterLockProvider(
                    provider.GetRequiredService<TStorage>(),
                    provider.GetService<IOutboxBus>(),
                    provider.GetRequiredService<DistributedLockOptions>(),
                    provider.GetRequiredService<ILongIdGenerator>(),
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<ILogger<DistributedReaderWriterLockProvider>>()
                )
            );

            services.TryAddSingleton<IDistributedReaderWriterLockProvider>(sp =>
                sp.GetRequiredService<DistributedReaderWriterLockProvider>()
            );

            return services;
        }
    }
}
