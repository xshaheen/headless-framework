// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Defines a type-safe message consumer. Implement this interface to handle messages of type <typeparamref name="TMessage"/>.
/// </summary>
/// <typeparam name="TMessage">The type of message to consume. Must be a reference type.</typeparam>
/// <remarks>
/// <para>
/// <strong>When to use:</strong>
/// <list type="bullet">
/// <item><description>New message handlers (recommended for all new code)</description></item>
/// <item><description>When you need compile-time verification of message types</description></item>
/// <item><description>When performance is critical (5-8x faster than reflection-based dispatch)</description></item>
/// <item><description>When you want explicit dependency injection and testability</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Example:</strong>
/// <code>
/// public sealed class OrderPlacedHandler : IConsume&lt;OrderPlacedEvent&gt;
/// {
///     private readonly IOrderRepository _orders;
///     private readonly ILogger&lt;OrderPlacedHandler&gt; _logger;
///
///     public OrderPlacedHandler(IOrderRepository orders, ILogger&lt;OrderPlacedHandler&gt; logger)
///     {
///         _orders = orders;
///         _logger = logger;
///     }
///
///     public async ValueTask Consume(ConsumeContext&lt;OrderPlacedEvent&gt; context, CancellationToken cancellationToken)
///     {
///         _logger.LogInformation("Processing order {OrderId}", context.Message.OrderId);
///         await _orders.CreateAsync(context.Message, cancellationToken);
///     }
/// }
/// </code>
/// </para>
/// </remarks>
public interface IConsume<TMessage>
    where TMessage : class
{
    /// <summary>Consumes and processes a message.</summary>
    /// <param name="context">The consumption context containing the message payload and metadata.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask Consume(ConsumeContext<TMessage> context, CancellationToken cancellationToken);
}
