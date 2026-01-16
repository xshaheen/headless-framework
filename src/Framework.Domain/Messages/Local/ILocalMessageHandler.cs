// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Domain;

public interface ILocalMessageHandler<in TMessage>
    where TMessage : class, ILocalMessage
{
    /// <summary>Handler handles the event by implementing this method.</summary>
    /// <param name="message">Message data</param>
    /// <param name="cancellationToken">Abort token</param>
    Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}
