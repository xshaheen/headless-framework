// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain.Messaging;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Domain;

/// <summary>
/// Handler for local (in-process) messages.
/// </summary>
public interface ILocalMessageHandler<in TMessage> : IConsumer<TMessage>
    where TMessage : class, ILocalMessage
{
    /// <summary>Handler handles the message by implementing this method.</summary>
    /// <param name="message">Message data</param>
    /// <param name="ct">Cancellation token</param>
    new Task ConsumeAsync(TMessage message, CancellationToken ct = default);

    Task IConsumer<TMessage>.ConsumeAsync(TMessage message, CancellationToken ct) => ConsumeAsync(message, ct);
}
