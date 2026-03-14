// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

internal sealed class RuntimeSubscriber(
    IRuntimeConsumerRegistry runtimeRegistry,
    MethodMatcherCache methodMatcherCache,
    IConsumerRegister consumerRegister,
    IBootstrapper bootstrapper,
    ILogger<RuntimeSubscriber> logger
) : IRuntimeSubscriber, IDisposable
{
    private readonly SemaphoreSlim _mutationLock = new(1, 1);

    public async ValueTask<RuntimeSubscriptionHandle> SubscribeAsync<TMessage>(
        RuntimeConsumeHandler<TMessage> handler,
        RuntimeSubscriptionOptions? options = null,
        CancellationToken cancellationToken = default
    )
        where TMessage : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = runtimeRegistry.Register(handler, options);
            methodMatcherCache.Invalidate();

            if (bootstrapper.IsStarted)
            {
                await consumerRegister.ReStartAsync(force: true).ConfigureAwait(false);
            }

            if (result.Status == RuntimeConsumerRegistrationStatus.Ignored)
            {
                return RuntimeSubscriptionHandle.Detached(
                    result.Topic,
                    result.Group,
                    result.HandlerId,
                    result.SubscriptionId
                );
            }

            logger.LogInformation(
                "Attached runtime subscription {SubscriptionId} for topic {Topic}, group {Group}, handler {HandlerId}.",
                result.SubscriptionId,
                result.Topic,
                result.Group,
                result.HandlerId
            );

            return RuntimeSubscriptionHandle.Attached(
                result.SubscriptionId!,
                result.Topic,
                result.Group,
                result.HandlerId,
                async () =>
                {
                    await UnsubscribeAsync(result.SubscriptionId!, CancellationToken.None).ConfigureAwait(false);
                }
            );
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async ValueTask<bool> UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var removed = runtimeRegistry.Unregister(subscriptionId);
            if (!removed)
            {
                return false;
            }

            methodMatcherCache.Invalidate();

            if (bootstrapper.IsStarted)
            {
                await consumerRegister.ReStartAsync(force: true).ConfigureAwait(false);
            }

            logger.LogInformation("Detached runtime subscription {SubscriptionId}.", subscriptionId);
            return true;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public void Dispose()
    {
        _mutationLock.Dispose();
    }
}
