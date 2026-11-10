// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Checks;
using Headless.Dashboard;
using Headless.Messaging.Internal;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Publishes the Messaging rows this node's senders and consumers changed to its own dashboard streams: it hears each
/// state write after it landed, reads the row back with its change marker, and publishes the row's projection with
/// that marker as the version.
/// </summary>
/// <remarks>
/// The pipelines report from inside their delivery paths, so a report only queues here and one background reader does
/// the reads: a slow store never holds a delivery. The queue is bounded and drops its oldest entry when full, because
/// a row it drops still reaches every stream through the change reader, as does every change another node made.
/// </remarks>
internal sealed class MessagingChangePublisher : IMessageStateObserver, IAsyncDisposable
{
    private const int _QueueCapacity = 10_000;

    private readonly MessagingStores _stores;
    private readonly IDashboardEventPublisher _publisher;
    private readonly ILogger<MessagingChangePublisher> _logger;
    private readonly Channel<(MessageType Type, Guid StorageId, string MessageId, MessagingStore Store)> _queue;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _startLock = new();
    private Task? _reader;

    public MessagingChangePublisher(
        MessagingStores stores,
        IDashboardEventPublisher publisher,
        ILogger<MessagingChangePublisher> logger
    )
    {
        _stores = Argument.IsNotNull(stores);
        _publisher = Argument.IsNotNull(publisher);
        _logger = Argument.IsNotNull(logger);
        _queue = Channel.CreateBounded<(MessageType, Guid, string, MessagingStore)>(
            new BoundedChannelOptions(_QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            }
        );
    }

    public void OnStateWritten(MessageType messageType, MediumMessage message)
    {
        // A row without a message id cannot be read back by it; the change reader still delivers it.
        if (
            MessagingViews.MessageIdOf(message) is not { } messageId
            || _stores.Of(message.OutboxStorage) is not { } store
            || !_queue.Writer.TryWrite((messageType, message.StorageId, messageId, store))
        )
        {
            return;
        }

        _EnsureReader();
    }

    /// <summary>
    /// Queues a row an operator command just changed on this node, so this node's streams show it without waiting for
    /// the change reader; the read-back runs off the command's request.
    /// </summary>
    public void Enqueue(MessageType type, Guid storageId, string messageId, MessagingStore store)
    {
        if (_queue.Writer.TryWrite((type, storageId, messageId, store)))
        {
            _EnsureReader();
        }
    }

    /// <summary>Reads a row back now and publishes it; does nothing when the row is not found.</summary>
    public async Task PublishAsync(
        MessageType type,
        Guid storageId,
        string messageId,
        MessagingStore store,
        CancellationToken cancellationToken
    )
    {
        var view = await MessagingViews
            .ReadViewAsync(store, type, storageId, messageId, cancellationToken)
            .ConfigureAwait(false);

        if (view is not { ChangedAt: { } changedAt } row)
        {
            return;
        }

        var id = new MessagingObjectId(_stores.NodeName, store.Name, storageId);

        _publisher.Publish(
            MessagingViews.Change(
                type,
                id,
                MessagingRowView.From(id, row),
                MessagingViews.VersionOf(changedAt),
                MessagingViews.AffectsAttention(row.StatusName)
            )
        );
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _stopping.CancelAsync().ConfigureAwait(false);

        Task? reader;

        lock (_startLock)
        {
            reader = _reader;
        }

        if (reader is not null)
        {
            try
            {
                await reader.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The host is stopping; queued rows still reach other nodes' streams through their change readers.
            }
        }

        _stopping.Dispose();
    }

    // Started on the first report, not at host start: a node whose pipelines never write starts no reader.
    private void _EnsureReader()
    {
        if (Volatile.Read(ref _reader) is not null)
        {
            return;
        }

        lock (_startLock)
        {
            _reader ??= Task.Run(() => _ReadAsync(_stopping.Token), CancellationToken.None);
        }
    }

    private async Task _ReadAsync(CancellationToken cancellationToken)
    {
        await foreach (
            var (type, storageId, messageId, store) in _queue
                .Reader.ReadAllAsync(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            try
            {
                await PublishAsync(type, storageId, messageId, store, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // The write already landed, so one failed read-back is logged and the reader serves the next report.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _logger.LogMessagingChangePublishFailed(exception, MessagingViews.KindOf(type), storageId, store.Name);
            }
        }
    }
}

internal static partial class MessagingChangePublisherLogs
{
    [LoggerMessage(
        EventId = 2130,
        EventName = "MessagingDashboardChangePublishFailed",
        Level = LogLevel.Warning,
        Message = "Messaging dashboard could not read back {Kind} row {StorageId} in store {Store} to publish it; the change reader still delivers it."
    )]
    internal static partial void LogMessagingChangePublishFailed(
        this ILogger logger,
        Exception exception,
        string kind,
        Guid storageId,
        string store
    );
}
