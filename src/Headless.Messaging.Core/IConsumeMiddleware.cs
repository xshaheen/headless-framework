// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Defines a middleware component that wraps a message consume operation.
/// </summary>
/// <typeparam name="TContext">
/// The consume context type this middleware handles. Use <see cref="ConsumeContext"/> to intercept
/// all consume operations or <c>ConsumeContext&lt;TMessage&gt;</c> to target a specific message type.
/// </typeparam>
/// <remarks>
/// Consume middleware is executed in priority order (lower value runs earlier) for every inbound
/// message that matches the registered context type. Calling the <c>next</c> delegate continues
/// the pipeline; omitting it short-circuits all subsequent middleware and the consumer handler.
/// Register via <c>MessagingBuilder.AddBusConsumeMiddleware&lt;T&gt;</c> or
/// <c>AddConsumeMiddlewareFor&lt;TMiddleware, TMessage&gt;</c>.
/// </remarks>
[PublicAPI]
public interface IConsumeMiddleware<in TContext>
    where TContext : ConsumeContext
{
    /// <summary>
    /// Invokes this middleware component, optionally advancing to the next middleware or consumer in the pipeline.
    /// </summary>
    /// <param name="context">The current consume context for the inbound message.</param>
    /// <param name="next">A delegate that invokes the next component in the pipeline. Must be called to continue processing.</param>
    ValueTask InvokeAsync(TContext context, Func<ValueTask> next);
}
