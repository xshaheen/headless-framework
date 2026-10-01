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
/// The messaging runtime calls the hook once the subscription receives messages: when the host starts, after every
/// rebuild of its consumer clients (a broker reconnect or a topology change), and after the transport re-establishes the
/// subscription on its own. Messages can arrive while the hook runs. The hook runs in its own service scope on a new
/// consumer instance, and an exception it throws is logged and does not stop the subscription.
/// </para>
/// <para>
/// Hooks of one subscription run one at a time, in establishment order. Host startup waits for the first ones; a rebuild
/// does not, so a hook may attach or detach a runtime subscription, which rebuilds the clients again. Each call is
/// bounded by <c>MessagingOptions.SubscriptionEstablishedTimeout</c> (30 seconds by default): when it expires the token
/// is canceled, the expiry is logged, and startup and later establishments continue without waiting for the hook.
/// </para>
/// <para>
/// The hook has no meaning on a competing consumer, whose durable subscription keeps its backlog, and the source
/// generator warns when a consumer that is not every-instance implements it.
/// </para>
/// </remarks>
[PublicAPI]
public interface IOnSubscriptionEstablished
{
    /// <summary>Called after the consumer's subscription is established or re-established in this process.</summary>
    /// <param name="context">Which subscription was established, and whether a gap preceded it.</param>
    /// <param name="cancellationToken">Cancelled when the subscription stops or the hook's time bound expires.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the consumer has resynchronized.</returns>
    ValueTask OnSubscriptionEstablishedAsync(
        SubscriptionEstablishedContext context,
        CancellationToken cancellationToken
    );
}

/// <summary>Describes one establishment of an every-instance subscription in this process.</summary>
/// <param name="ConsumerIdentity">The identity of the consumer whose subscription was established.</param>
/// <param name="MessageNames">The message names the subscription receives.</param>
/// <param name="IsReconnect">
/// <see langword="false"/> for the first establishment in this process; <see langword="true"/> when an earlier
/// subscription of this consumer existed, so messages published between the two may never have arrived.
/// </param>
/// <param name="Generation">
/// Counts the establishments of this consumer's subscription in this process, starting at 1.
/// </param>
[PublicAPI]
public sealed record SubscriptionEstablishedContext(
    string ConsumerIdentity,
    IReadOnlyList<string> MessageNames,
    bool IsReconnect,
    long Generation
);
