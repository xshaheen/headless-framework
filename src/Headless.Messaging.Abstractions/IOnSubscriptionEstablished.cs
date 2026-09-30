// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Optional hook for an every-instance Bus consumer, called when the process's subscription is established, including
/// after it is re-established.
/// </summary>
/// <remarks>
/// An every-instance subscription (<see cref="BusConsumerAttribute.EveryInstance"/>) has no backlog: messages published
/// while the process was not subscribed never reach it. A consumer that mirrors state, such as an in-memory cache,
/// implements this hook to resynchronize or discard that state once it is subscribed again.
/// <para>
/// The hook has no meaning on a competing consumer, whose durable subscription keeps its backlog, and the source
/// generator warns when a consumer that is not every-instance implements it.
/// </para>
/// </remarks>
[PublicAPI]
public interface IOnSubscriptionEstablished
{
    /// <summary>Called after the consumer's subscription is established or re-established in this process.</summary>
    /// <param name="cancellationToken">Cancelled when the host stops.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the consumer has resynchronized.</returns>
    ValueTask OnSubscriptionEstablishedAsync(CancellationToken cancellationToken);
}
