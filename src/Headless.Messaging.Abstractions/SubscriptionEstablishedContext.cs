// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

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
