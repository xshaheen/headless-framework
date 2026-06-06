// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;
using Headless.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

[PublicAPI]
public static class AddDistributedReadWriteLockExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDistributedReadWriteLock<TStorage>(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
            where TStorage : class, IDistributedReadWriteLockStorage
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(optionSetupAction);

            return services._AddDistributedReadWriteLockCore<TStorage>();
        }

        public IServiceCollection AddDistributedReadWriteLock<TStorage>(
            Action<DistributedLockOptions> optionSetupAction
        )
            where TStorage : class, IDistributedReadWriteLockStorage
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(optionSetupAction);

            return services._AddDistributedReadWriteLockCore<TStorage>();
        }

        public IServiceCollection AddDistributedReadWriteLock<TStorage>(IConfiguration config)
            where TStorage : class, IDistributedReadWriteLockStorage
        {
            services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(config);

            return services._AddDistributedReadWriteLockCore<TStorage>();
        }

        private IServiceCollection _AddDistributedReadWriteLockCore<TStorage>()
            where TStorage : class, IDistributedReadWriteLockStorage
        {
            services.TryAddSingleton<TStorage>();
            services.AddSingletonOptionValue<DistributedLockOptions>();
            services.TryAddSingleton(TimeProvider.System);
            services.AddHeadlessGuidGenerator();

            services.TryAddSingleton<DistributedReadWriteLock>(provider => new DistributedReadWriteLock(
                provider.GetRequiredService<TStorage>(),
                provider.GetService<IOutboxBus>(),
                provider.GetRequiredService<DistributedLockOptions>(),
                provider.GetRequiredService<IGuidGenerator>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<DistributedReadWriteLock>>()
            ));

            services.TryAddSingleton<IDistributedReadWriteLock>(sp =>
                sp.GetRequiredService<DistributedReadWriteLock>()
            );

            return services;
        }
    }
}
