// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal static class DistributedLockConsumerRegistration
{
    public static void TryAddLockReleasedConsumer(IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(IOutboxBus)))
        {
            return;
        }

        if (_HasBuiltInConsumerMetadata(services))
        {
            return;
        }

        if (services.Any(d => d.ServiceType == typeof(IConsume<DistributedLockReleased>)))
        {
            services.TryAddSingleton<DistributedLockReleasedConsumerConflict>();

            return;
        }

        services
            .AddBusConsumer<DistributedLockProvider.LockReleasedConsumer, DistributedLockReleased>(
                "headless.locks.released"
            )
            .Concurrency(1);
    }

    private static bool _HasBuiltInConsumerMetadata(IServiceCollection services)
    {
        return services.Any(d =>
            d.ServiceType == typeof(ConsumerMetadata)
            && d.ImplementationInstance is ConsumerMetadata metadata
            && metadata.ConsumerType == typeof(DistributedLockProvider.LockReleasedConsumer)
        );
    }
}

internal sealed class DistributedLockReleasedConsumerConflict;
