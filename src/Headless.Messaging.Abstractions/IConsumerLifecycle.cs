// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Provides per-dispatch lifecycle hooks for consumer initialization and cleanup.
/// Implement this interface on your <see cref="IConsume{TMessage}"/> consumers to receive notifications
/// immediately before and after each message is handled by the scoped consumer instance.
/// </summary>
/// <remarks>
/// <para>
/// <strong>When to use:</strong>
/// <list type="bullet">
/// <item><description>Initialize per-dispatch resources before the message is handled</description></item>
/// <item><description>Clean up per-dispatch resources after the handler completes or fails</description></item>
/// <item><description>Open short-lived connections or scopes that should not leak across deliveries</description></item>
/// <item><description>Record delivery-specific diagnostics around the handler invocation</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Lifecycle timeline:</strong>
/// <list type="number">
/// <item><description>A new DI scope is created for the delivery</description></item>
/// <item><description>The consumer instance is resolved from that scope</description></item>
/// <item><description><see cref="OnStartingAsync"/> is called (if the consumer implements this interface)</description></item>
/// <item><description><see cref="IConsume{TMessage}.Consume"/> is invoked</description></item>
/// <item><description><see cref="OnStoppingAsync"/> is called in a <c>finally</c> block (if the consumer implements this interface)</description></item>
/// <item><description>The DI scope is disposed</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Example:</strong>
/// <code>
/// public sealed class OrderProcessor : IConsume&lt;OrderPlaced&gt;, IConsumerLifecycle
/// {
///     private readonly IOrderRepository _repository;
///     private readonly ILogger&lt;OrderProcessor&gt; _logger;
///     private HttpClient? _httpClient;
///
///     public OrderProcessor(IOrderRepository repository, ILogger&lt;OrderProcessor&gt; logger)
///     {
///         _repository = repository;
///         _logger = logger;
///     }
///
///     public async ValueTask OnStartingAsync(CancellationToken cancellationToken)
///     {
///         _logger.LogInformation("Preparing order processor for a message");
///         _httpClient = new HttpClient { BaseAddress = new Uri("https://api.example.com") };
///         await _repository.PreloadActiveOrdersAsync(cancellationToken);
///     }
///
///     public async ValueTask Consume(ConsumeContext&lt;OrderPlaced&gt; context, CancellationToken cancellationToken)
///     {
///         _logger.LogInformation("Processing order {OrderId}", context.Message.OrderId);
///         await _repository.CreateAsync(context.Message, cancellationToken);
///     }
///
///     public async ValueTask OnStoppingAsync(CancellationToken cancellationToken)
///     {
///         _logger.LogInformation("Cleaning up order processor after a message");
///         await _repository.FlushAsync(cancellationToken);
///         _httpClient?.Dispose();
///     }
/// }
/// </code>
/// </para>
/// </remarks>
public interface IConsumerLifecycle
{
    /// <summary>
    /// Called before the current message is processed by the scoped consumer instance.
    /// Use this to initialize per-dispatch resources or perform lightweight setup for the current delivery.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the startup operation.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous startup operation.</returns>
    /// <remarks>
    /// <para>
    /// This method is called once per dispatch after the consumer instance has been resolved from the per-message DI scope.
    /// If this method throws an exception, message handling stops and the exception is propagated.
    /// </para>
    /// <para>
    /// <strong>Best practices:</strong>
    /// <list type="bullet">
    /// <item><description>Keep initialization logic fast to avoid delaying message handling</description></item>
    /// <item><description>Use the cancellation token to support graceful cancellation</description></item>
    /// <item><description>Log important initialization steps for debugging</description></item>
    /// <item><description>Avoid keeping cross-delivery mutable state on scoped consumers</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    ValueTask OnStartingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Called after the current message finishes processing, even when <see cref="IConsume{TMessage}.Consume"/> throws.
    /// Use this to clean up per-dispatch resources, flush buffers, or perform delivery-scoped teardown.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the shutdown operation.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous shutdown operation.</returns>
    /// <remarks>
    /// <para>
    /// This method is called once per dispatch in a <c>finally</c> block.
    /// If this method throws an exception, it is suppressed so cleanup does not mask the original message failure.
    /// </para>
    /// <para>
    /// <strong>Best practices:</strong>
    /// <list type="bullet">
    /// <item><description>Complete cleanup quickly to avoid delaying the next message</description></item>
    /// <item><description>Use the cancellation token to support graceful cancellation</description></item>
    /// <item><description>Flush any pending work or buffered data for the current delivery</description></item>
    /// <item><description>Dispose of temporary resources created during <see cref="OnStartingAsync"/></description></item>
    /// <item><description>Log important teardown steps for debugging</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    ValueTask OnStoppingAsync(CancellationToken cancellationToken);
}
