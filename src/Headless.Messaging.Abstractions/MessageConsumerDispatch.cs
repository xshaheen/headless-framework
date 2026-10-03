// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Headless.Reliability;

namespace Headless.Messaging;

/// <summary>
/// Runs one attribute-declared consumer for one delivery. The Messaging source generator emits one per consumer class:
/// it builds the class from the delivery's service scope, switches on the message type of <paramref name="context"/>, and
/// calls the matching <see cref="IConsume{TMessage}.ConsumeAsync"/> with the typed context. For a request the class
/// answers, it calls <see cref="IRespond{TRequest, TResponse}.RespondAsync"/> instead and records the returned value on
/// <paramref name="context"/> as the reply.
/// </summary>
/// <param name="services">The service provider of the delivery's scope.</param>
/// <param name="context">The typed <see cref="ConsumeContext{TMessage}"/> of the delivery.</param>
/// <param name="cancellationToken">Cancelled when the delivery is abandoned.</param>
/// <returns>A <see cref="ValueTask"/> that completes when the consumer has handled the message.</returns>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate ValueTask MessageConsumerDispatch(
    IServiceProvider services,
    ConsumeContext context,
    CancellationToken cancellationToken
);
