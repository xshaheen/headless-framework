// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

[PublicAPI]
public static class MessageSubscriberExtensions
{
    public static Task SubscribeAsync<TPayload>(
        this IMessageSubscriber subscriber,
        Func<TPayload, Task> handler,
        CancellationToken cancellationToken = default
    )
        where TPayload : class
    {
        return subscriber.SubscribeAsync<TPayload>((msg, _) => handler(msg.Payload), cancellationToken);
    }

    public static Task SubscribeAsync<TPayload>(
        this IMessageSubscriber subscriber,
        Func<IMessageSubscribeMedium<TPayload>, Task> handler,
        CancellationToken cancellationToken = default
    )
        where TPayload : class
    {
        return subscriber.SubscribeAsync<TPayload>((msg, _) => handler(msg), cancellationToken);
    }

    public static Task SubscribeAsync<TPayload>(
        this IMessageSubscriber subscriber,
        Action<IMessageSubscribeMedium<TPayload>> handler,
        CancellationToken cancellationToken = default
    )
        where TPayload : class
    {
        return subscriber.SubscribeAsync<TPayload>(
            (msg, _) =>
            {
                handler(msg);
                return Task.CompletedTask;
            },
            cancellationToken
        );
    }
}
