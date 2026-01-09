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
    extension(IMessageSubscriber subscriber)
    {
        public Task SubscribeAsync<T>(
            Func<IMessageSubscribeMedium<T>, Task> handler,
            CancellationToken cancellationToken = default
        )
            where T : class
        {
            return subscriber.SubscribeAsync<T>((medium, _) => handler(medium), cancellationToken);
        }

        public Task SubscribeAsync<T>(
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

        public Task SubscribeAsync<T>(Func<T, Task> handler, CancellationToken cancellationToken = default)
            where T : class
        {
            return subscriber.SubscribeAsync<T>((medium, _) => handler(medium.Payload), cancellationToken);
        }

        public Task SubscribeAsync<T>(
            Func<T, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default
        )
            where T : class
        {
            return subscriber.SubscribeAsync<T>((medium, token) => handler(medium.Payload, token), cancellationToken);
        }

        public Task SubscribeAsync<T>(Action<T> handler, CancellationToken cancellationToken = default)
            where T : class
        {
            return subscriber.SubscribeAsync<T>(
                (medium, _) =>
                {
                    handler(medium.Payload);

                    return Task.CompletedTask;
                },
                cancellationToken
            );
        }
    }
}
