// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

internal static class DistributedReadWriteLockCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="DistributedReadWriteLock"/> singleton and its
    /// <see cref="IDistributedReadWriteLock"/> alias using <typeparamref name="TStorage"/> as
    /// the storage backend. Registrations are idempotent via <c>TryAdd*</c>.
    /// </summary>
    internal static IServiceCollection AddDistributedReadWriteLockCore<TStorage>(this IServiceCollection services)
        where TStorage : class, IDistributedReadWriteLockStorage
    {
        services.TryAddSingleton<TStorage>();
        services.AddSingletonOptionValue<DistributedLockOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHeadlessGuidGenerator();

        services.TryAddSingleton<DistributedReadWriteLock>(provider => new DistributedReadWriteLock(
            provider.GetRequiredService<TStorage>(),
            provider.GetService<IBus>(),
            provider.GetRequiredService<DistributedLockOptions>(),
            provider.GetRequiredService<IGuidGenerator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<DistributedReadWriteLock>>()
        ));

        services.TryAddSingleton<IDistributedReadWriteLock>(sp => sp.GetRequiredService<DistributedReadWriteLock>());

        return services;
    }
}
