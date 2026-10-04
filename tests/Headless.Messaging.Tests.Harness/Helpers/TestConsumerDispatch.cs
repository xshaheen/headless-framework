// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Helpers;

/// <summary>
/// Dispatch for hand-written <see cref="IMessagingModule"/> types in test hosts. A suite writes its own module rather
/// than adding its assembly's generated one, so each host registers only the consumers it needs, under the identities
/// it needs.
/// </summary>
[PublicAPI]
public static class TestConsumerDispatch
{
    /// <summary>
    /// Resolves the consumer from the delivery's scope when the test registered it, and builds one otherwise. Test
    /// consumers often record deliveries on the singleton instance the test asserts on, which the generated dispatch,
    /// always building a fresh instance, would bypass.
    /// </summary>
    public static MessageConsumerDispatch FromServices<TConsumer, TMessage>()
        where TConsumer : class, IConsume<TMessage>
        where TMessage : class =>
        static (services, context, cancellationToken) =>
            ActivatorUtilities
                .GetServiceOrCreateInstance<TConsumer>(services)
                .ConsumeAsync((ConsumeContext<TMessage>)context, cancellationToken);
}
