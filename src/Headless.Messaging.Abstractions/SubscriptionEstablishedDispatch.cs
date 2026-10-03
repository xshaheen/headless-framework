// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Headless.Reliability;

namespace Headless.Messaging;

/// <summary>
/// Runs <see cref="IOnSubscriptionEstablished.OnSubscriptionEstablishedAsync"/> on one attribute-declared every-instance
/// consumer. The Messaging source generator emits one per consumer class that implements the hook: it builds the class
/// the same way its <see cref="MessageConsumerDispatch"/> does, calls the hook, and releases the instance it created.
/// </summary>
/// <param name="services">The service provider of the hook's scope.</param>
/// <param name="context">Which subscription was established.</param>
/// <param name="cancellationToken">Cancelled when the subscription stops or the hook's time bound expires.</param>
/// <returns>A <see cref="ValueTask"/> that completes when the consumer has resynchronized.</returns>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate ValueTask SubscriptionEstablishedDispatch(
    IServiceProvider services,
    SubscriptionEstablishedContext context,
    CancellationToken cancellationToken
);
