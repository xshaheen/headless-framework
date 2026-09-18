// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Defines a middleware component that wraps the receive stage of an inbound message, before
/// contract-version validation and deserialization.
/// </summary>
/// <remarks>
/// Receive middleware is executed in priority order (lower value runs earlier) for every inbound
/// delivery whose consumer was resolved. Calling the <c>next</c> delegate continues the receive
/// pipeline into contract-version validation and deserialization; omitting it short-circuits the
/// consumer, so the middleware must declare an explicit outcome via <see cref="ReceiveContext.Skip"/>
/// or <see cref="ReceiveContext.Reject"/> before returning — a return with neither is treated as a
/// reject. Register via <c>MessagingBuilder.AddReceiveMiddleware&lt;T&gt;</c> to intercept every
/// delivery, or <c>AddReceiveMiddlewareFor&lt;TMiddleware, TMessage&gt;</c> to target a specific payload
/// type, consumer group, and lane.
/// </remarks>
[PublicAPI]
public interface IReceiveMiddleware
{
    /// <summary>
    /// Invokes this middleware component, optionally advancing to the next middleware or the inner receive pipeline.
    /// </summary>
    /// <param name="context">The current receive context for the inbound raw envelope.</param>
    /// <param name="next">
    /// A delegate that invokes the next component in the pipeline — the remaining receive middleware,
    /// then contract-version validation, deserialization, and the null-payload check. Must be called
    /// to continue processing.
    /// </param>
    ValueTask InvokeAsync(ReceiveContext context, Func<ValueTask> next);
}
