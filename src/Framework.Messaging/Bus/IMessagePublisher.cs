// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

[PublicAPI]
public interface IMessagePublisher
{
    Task PublishAsync<T>(
        T message,
        PublishMessageOptions? options = null,
        CancellationToken cancellationToken = default
    )
        where T : class;
}

public static class IMessagePublisherExtensions
{
    public static Task PublishAsync<T>(
        this IMessagePublisher publisher,
        T message,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        return publisher.PublishAsync(message, options: null, cancellationToken);
    }
}
