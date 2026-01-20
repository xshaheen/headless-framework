// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages;

/// <summary>
/// Provides lifecycle hooks for consumer initialization and cleanup.
/// Implement this interface on your <see cref="IConsume{TMessage}"/> consumers to receive notifications
/// when the consumer starts up or shuts down.
/// </summary>
/// <remarks>
/// <para>
/// <strong>When to use:</strong>
/// <list type="bullet">
/// <item><description>Initialize resources when the consumer starts (database connections, file handles, caches)</description></item>
/// <item><description>Clean up resources when the consumer stops (close connections, flush buffers, dispose objects)</description></item>
/// <item><description>Perform warm-up operations (preload data, establish connections)</description></item>
/// <item><description>Register shutdown handlers or perform graceful shutdown logic</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Lifecycle timeline:</strong>
/// <list type="number">
/// <item><description>Consumer class is resolved from DI container</description></item>
/// <item><description><see cref="OnStartingAsync"/> is called (if consumer implements this interface)</description></item>
/// <item><description>Consumer processes messages via <see cref="IConsume{TMessage}.Consume"/></description></item>
/// <item><description>Application shutdown begins</description></item>
/// <item><description><see cref="OnStoppingAsync"/> is called (if consumer implements this interface)</description></item>
/// <item><description>Consumer instance is disposed (if implements <see cref="IDisposable"/> or <see cref="IAsyncDisposable"/>)</description></item>
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
///         _logger.LogInformation("Initializing order processor");
///         _httpClient = new HttpClient { BaseAddress = new Uri("https://api.example.com") };
///
///         // Warm up cache or database
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
///         _logger.LogInformation("Shutting down order processor");
///
///         // Flush any pending operations
///         await _repository.FlushAsync(cancellationToken);
///
///         // Dispose resources
///         _httpClient?.Dispose();
///     }
/// }
/// </code>
/// </para>
/// </remarks>
public interface IConsumerLifecycle
{
    /// <summary>
    /// Called when the consumer is starting up, before any messages are processed.
    /// Use this to initialize resources, establish connections, or perform warm-up operations.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the startup operation.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous startup operation.</returns>
    /// <remarks>
    /// <para>
    /// This method is called once per consumer instance when the messaging system starts.
    /// If this method throws an exception, the consumer will not start and the exception will be logged.
    /// </para>
    /// <para>
    /// <strong>Best practices:</strong>
    /// <list type="bullet">
    /// <item><description>Keep initialization logic fast to avoid delaying application startup</description></item>
    /// <item><description>Use the cancellation token to support graceful cancellation</description></item>
    /// <item><description>Log important initialization steps for debugging</description></item>
    /// <item><description>Consider using lazy initialization for expensive resources</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    ValueTask OnStartingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Called when the consumer is stopping, after message processing has ceased.
    /// Use this to clean up resources, flush buffers, or perform graceful shutdown operations.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the shutdown operation.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous shutdown operation.</returns>
    /// <remarks>
    /// <para>
    /// This method is called once per consumer instance when the messaging system is shutting down.
    /// If this method throws an exception, it will be logged but will not prevent shutdown from continuing.
    /// </para>
    /// <para>
    /// <strong>Best practices:</strong>
    /// <list type="bullet">
    /// <item><description>Complete shutdown quickly to avoid delaying application shutdown</description></item>
    /// <item><description>Use the cancellation token to support graceful cancellation</description></item>
    /// <item><description>Flush any pending work or buffered data</description></item>
    /// <item><description>Dispose of resources (though <see cref="IDisposable"/> or <see cref="IAsyncDisposable"/> is preferred for this)</description></item>
    /// <item><description>Log important shutdown steps for debugging</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    ValueTask OnStoppingAsync(CancellationToken cancellationToken);
}
