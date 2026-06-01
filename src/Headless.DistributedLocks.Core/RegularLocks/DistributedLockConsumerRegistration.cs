// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal static class DistributedLockConsumerRegistration
{
    public static void TryAddLockReleasedConsumer(IServiceCollection services)
    {
        if (
            services.Any(d => d.ServiceType == typeof(IOutboxBus))
            && !services.Any(d => d.ServiceType == typeof(IConsume<DistributedLockReleased>))
        )
        {
            services
                .AddBusConsumer<DistributedLockProvider.LockReleasedConsumer, DistributedLockReleased>(
                    "headless.locks.released"
                )
                .Concurrency(1);
        }
    }
}
