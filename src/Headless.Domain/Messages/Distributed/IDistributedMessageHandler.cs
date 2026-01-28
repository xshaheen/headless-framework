// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

public interface IDistributedMessageHandler<in TMessage>
    where TMessage : class, IDistributedMessage
{
    /// <summary>Handler handles the event by implementing this method.</summary>
    ValueTask HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}
