// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNetCore.CAP;
using Framework.Domains;

namespace Framework.Messaging;

public sealed class CapDistributedMessagePublisher(ICapPublisher publisher) : IDistributedMessagePublisher
{
    public void Publish<T>(T message)
        where T : class, IDistributedMessage
    {
        var name = MessageName.GetFrom<T>();
        publisher.Publish(name, contentObj: message, callbackName: null);
    }

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class, IDistributedMessage
    {
        var name = MessageName.GetFrom<T>();

        return publisher.PublishAsync(name, contentObj: message, callbackName: null, cancellationToken);
    }
}
