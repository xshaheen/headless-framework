// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Domains;

public interface IDistributedMessagePublisher
{
    void Publish<T>(T message)
        where T : class, IDistributedMessage;

    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class, IDistributedMessage;
}
