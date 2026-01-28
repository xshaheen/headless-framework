// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

public interface IDistributedMessagePublisher
{
    ValueTask PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class, IDistributedMessage;
}
