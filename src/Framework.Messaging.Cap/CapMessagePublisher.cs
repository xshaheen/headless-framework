// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNetCore.CAP;

namespace Framework.Messaging;

public sealed class CapMessagePublisher(ICapPublisher publisher) : IMessagePublisher
{
    public Task PublishAsync<T>(
        T message,
        PublishMessageOptions? options = null,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        var name = MessageName.GetFrom<T>();

        return publisher.PublishAsync(name, message, callbackName: null, cancellationToken);
    }
}
