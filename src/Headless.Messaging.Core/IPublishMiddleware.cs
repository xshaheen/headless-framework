// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Defines a middleware component that wraps a message publish operation.
/// </summary>
/// <typeparam name="TContext">
/// The publish context type this middleware handles. Use <see cref="PublishContext"/> to intercept
/// all publish operations or <c>PublishContext&lt;TMessage&gt;</c> to target a specific message type.
/// </typeparam>
/// <remarks>
/// Publish middleware is executed in priority order (lower value runs earlier) for every outbound
/// message that matches the registered context type. Calling the <c>next</c> delegate continues
/// the pipeline; omitting it short-circuits all subsequent middleware and the inner publisher.
/// Register via <c>MessagingBuilder.AddBusPublishMiddleware&lt;T&gt;</c> or
/// <c>AddPublishMiddlewareFor&lt;TMiddleware, TMessage&gt;</c>.
/// </remarks>
[PublicAPI]
public interface IPublishMiddleware<in TContext>
    where TContext : PublishContext
{
    /// <summary>
    /// Invokes this middleware component, optionally advancing to the next middleware or publisher in the pipeline.
    /// </summary>
    /// <param name="context">The current publish context for the outbound message.</param>
    /// <param name="next">A delegate that invokes the next component in the pipeline. Must be called to continue publishing.</param>
    ValueTask InvokeAsync(TContext context, Func<ValueTask> next);
}
