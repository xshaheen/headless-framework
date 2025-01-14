// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

[PublicAPI]
public interface IMessageSubscriber
{
    Task SubscribeAsync<TPayload>(
        Func<IMessageSubscribeMedium<TPayload>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default
    )
        where TPayload : class;
}

[PublicAPI]
public static class MessageSubscriberExtensions
{
    public static Task SubscribeAsync<T>(
        this IMessageSubscriber subscriber,
        Func<IMessageSubscribeMedium<T>, Task> handler,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        return subscriber.SubscribeAsync<T>((medium, _) => handler(medium), cancellationToken);
    }

    public static Task SubscribeAsync<T>(
        this IMessageSubscriber subscriber,
        Action<IMessageSubscribeMedium<T>> handler,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        return subscriber.SubscribeAsync<T>(
            (msg, _) =>
            {
                handler(msg);

                return Task.CompletedTask;
            },
            cancellationToken
        );
    }

    public static Task SubscribeAsync<T>(
        this IMessageSubscriber subscriber,
        Func<T, Task> handler,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        return subscriber.SubscribeAsync<T>((medium, _) => handler(medium.Payload), cancellationToken);
    }

    public static Task SubscribeAsync<T>(
        this IMessageSubscriber subscriber,
        Func<T, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        return subscriber.SubscribeAsync<T>((medium, token) => handler(medium.Payload, token), cancellationToken);
    }

    public static Task SubscribeAsync<T>(
        this IMessageSubscriber subscriber,
        Action<T> handler,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        return subscriber.SubscribeAsync<T>(
            (msg, _) =>
            {
                handler(msg.Payload);

                return Task.CompletedTask;
            },
            cancellationToken
        );
    }
}
