// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Transactions;

/// <summary>
/// A thread-safe holder for storing the current outbox transaction context within a scope (e.g., per HTTP request or async execution context).
/// This is used internally to associate a transaction with the ambient execution context.
/// </summary>
internal sealed class OutboxTransactionHolder
{
    /// <summary>
    /// Gets or sets the outbox transaction associated with the current context.
    /// </summary>
    public IOutboxTransaction? Transaction { get; set; }
}

/// <summary>
/// Provides a base implementation of <see cref="IOutboxTransaction"/> that manages message publishing within a database transaction.
/// This class handles buffering, flushing, and coordination of messages with the message transport layer.
/// </summary>
/// <remarks>
/// This base class:
/// <list type="bullet">
/// <item><description>Maintains an internal queue of messages to be published.</description></item>
/// <item><description>Provides methods to add messages to the queue and flush them to the dispatcher.</description></item>
/// <item><description>Handles both delayed and immediate message publishing based on message headers.</description></item>
/// <item><description>Integrates with the dispatcher to enqueue messages for publishing or scheduling.</description></item>
/// </list>
/// Derived classes must implement the transaction-specific Commit/Rollback operations.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="OutboxTransaction"/> class with a dispatcher.
/// </remarks>
/// <param name="dispatcher">The dispatcher used to enqueue messages for publishing and execution.</param>
public abstract class OutboxTransaction(IDispatcher dispatcher) : IOutboxTransaction
{
    private readonly ConcurrentQueue<MediumMessage> _bufferList = new();

    /// <summary>
    /// Gets or sets a value indicating whether this transaction is automatically committed after a message is published.
    /// When true, the transaction commits immediately; when false, manual commit is required.
    /// </summary>
    public bool AutoCommit { get; set; }

    /// <summary>
    /// Gets or sets the underlying database transaction object.
    /// This can be cast to the specific database transaction type (e.g., SqlTransaction, NpgsqlTransaction) when needed.
    /// </summary>
    public virtual object? DbTransaction { get; set; }

    /// <summary>
    /// Commits the transaction synchronously, causing all buffered messages to be sent to the message queue.
    /// </summary>
    public abstract void Commit();

    /// <summary>
    /// Asynchronously commits the transaction, causing all buffered messages to be sent to the message queue.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous commit operation.</returns>
    public abstract Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the transaction synchronously, discarding all buffered messages without sending them.
    /// </summary>
    public abstract void Rollback();

    /// <summary>
    /// Asynchronously rolls back the transaction, discarding all buffered messages without sending them.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous rollback operation.</returns>
    public abstract Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a message to the internal buffer queue to be sent when the transaction is committed.
    /// This is typically called when publishing a message within a transaction context.
    /// </summary>
    /// <param name="msg">The message to add to the buffer.</param>
    protected internal virtual void AddToSent(MediumMessage msg)
    {
        _bufferList.Enqueue(msg);
    }

    /// <summary>
    /// Synchronously flushes all buffered messages from the internal queue to the dispatcher.
    /// This method blocks until all messages have been enqueued.
    /// </summary>
    protected virtual void Flush()
    {
        FlushAsync().AnyContext().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously flushes all buffered messages from the internal queue to the dispatcher.
    /// Delayed messages are enqueued to the scheduler; immediate messages are enqueued for publishing.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous flush operation.</returns>
    protected virtual async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        while (!_bufferList.IsEmpty)
        {
            if (_bufferList.TryDequeue(out var message))
            {
                var isDelayMessage = message.Origin.Headers.ContainsKey(Headers.DelayTime);
                if (isDelayMessage)
                {
                    await dispatcher
                        .EnqueueToScheduler(
                            message,
                            DateTime.Parse(message.Origin.Headers[Headers.SentTime]!, CultureInfo.InvariantCulture),
                            transaction: null,
                            cancellationToken
                        )
                        .AnyContext();
                }
                else
                {
                    await dispatcher.EnqueueToPublish(message, cancellationToken).AnyContext();
                }
            }
        }
    }

    /// <summary>
    /// Disposes the transaction, releasing the underlying database transaction if it implements <see cref="IDisposable"/>.
    /// </summary>
    public virtual void Dispose()
    {
        (DbTransaction as IDisposable)?.Dispose();
        DbTransaction = null;
    }
}
