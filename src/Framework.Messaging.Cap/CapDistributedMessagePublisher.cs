// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using DotNetCore.CAP;
using Framework.Kernel.Domains;

namespace Framework.Messaging;

public sealed class CapDistributedMessagePublisher(ICapPublisher publisher) : IDistributedMessagePublisher
{
    public void Publish<T>(T message)
        where T : class, IDistributedMessage
    {
        publisher.Publish(name: _GetMessageName(typeof(T)), contentObj: message, callbackName: null);
    }

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class, IDistributedMessage
    {
        return publisher.PublishAsync(
            name: _GetMessageName(typeof(T)),
            contentObj: message,
            callbackName: null,
            cancellationToken
        );
    }

    private static string _GetMessageName(Type type)
    {
        return type.GetCustomAttribute<DistributedMessageAttribute>()?.MessageName
            ?? throw new InvalidOperationException(
                "Message name is not defined. Please use DistributedMessageAttribute to define the message name."
            );
    }
}
