// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

public interface ILocalMessagePublisher
{
    void Publish<T>(T message)
        where T : class, ILocalMessage;

    ValueTask PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class, ILocalMessage;
}
