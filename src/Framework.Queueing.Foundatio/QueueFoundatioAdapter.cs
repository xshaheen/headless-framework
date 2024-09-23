// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics.CodeAnalysis;
using Framework.Kernel.BuildingBlocks.Helpers.System;
using Framework.Queueing.Internals;

namespace Framework.Queueing;

public sealed class QueueFoundatioAdapter<T> : IQueue<T>
    where T : class
{
    private readonly Foundatio.Queues.IQueue<T> _foundatio;

    public QueueFoundatioAdapter(Foundatio.Queues.IQueue<T> foundatio)
    {
        _foundatio = foundatio;
        QueueId = foundatio.QueueId;

        Enqueuing = new FoundatioAsyncEventAdapter<Foundatio.Queues.EnqueuingEventArgs<T>, EnqueuingEventArgs<T>>(
            _foundatio.Enqueuing,
            args =>
                new()
                {
                    Cancel = args.Cancel,
                    Queue = this,
                    Data = args.Data,
                    Options = _MapOptions(args.Options),
                },
            args =>
                new()
                {
                    Cancel = args.Cancel,
                    Data = args.Data,
                    Options = _MapOptions(args.Options),
                    Queue = _foundatio,
                }
        );

        Enqueued = new FoundatioAsyncEventAdapter<Foundatio.Queues.EnqueuedEventArgs<T>, EnqueuedEventArgs<T>>(
            _foundatio.Enqueued,
            args => new() { Entry = _MapEntry(args.Entry), Queue = this },
            args => new() { Entry = _MapEntry(args.Entry), Queue = _foundatio }
        );

        Dequeued = new FoundatioAsyncEventAdapter<Foundatio.Queues.DequeuedEventArgs<T>, DequeuedEventArgs<T>>(
            _foundatio.Dequeued,
            args => new() { Entry = _MapEntry(args.Entry), Queue = this },
            args => new() { Entry = _MapEntry(args.Entry), Queue = _foundatio }
        );

        LockRenewed = new FoundatioAsyncEventAdapter<Foundatio.Queues.LockRenewedEventArgs<T>, LockRenewedEventArgs<T>>(
            _foundatio.LockRenewed,
            args => new() { Entry = _MapEntry(args.Entry), Queue = this },
            args => new() { Entry = _MapEntry(args.Entry), Queue = _foundatio }
        );

        Completed = new FoundatioAsyncEventAdapter<Foundatio.Queues.CompletedEventArgs<T>, CompletedEventArgs<T>>(
            _foundatio.Completed,
            args => new() { Entry = _MapEntry(args.Entry), Queue = this },
            args => new() { Entry = _MapEntry(args.Entry), Queue = _foundatio }
        );

        Abandoned = new FoundatioAsyncEventAdapter<Foundatio.Queues.AbandonedEventArgs<T>, AbandonedEventArgs<T>>(
            _foundatio.Abandoned,
            args => new() { Entry = _MapEntry(args.Entry), Queue = this },
            args => new() { Entry = _MapEntry(args.Entry), Queue = _foundatio }
        );
    }

    public string QueueId { get; }

    public IAsyncEvent<EnqueuingEventArgs<T>> Enqueuing { get; }

    public IAsyncEvent<EnqueuedEventArgs<T>> Enqueued { get; }

    public IAsyncEvent<DequeuedEventArgs<T>> Dequeued { get; }

    public IAsyncEvent<LockRenewedEventArgs<T>> LockRenewed { get; }

    public IAsyncEvent<CompletedEventArgs<T>> Completed { get; }

    public IAsyncEvent<AbandonedEventArgs<T>> Abandoned { get; }

    public Task<string> EnqueueAsync(T data, QueueEntryOptions? options = null)
    {
        return _foundatio.EnqueueAsync(data, _MapOptions(options));
    }

    public async Task<IQueueEntry<T>> DequeueAsync(CancellationToken cancellationToken)
    {
        var entry = await _foundatio.DequeueAsync(cancellationToken);

        return _MapEntry(entry);
    }

    public async Task<IQueueEntry<T>> DequeueAsync(TimeSpan? timeout = null)
    {
        var entry = await _foundatio.DequeueAsync(timeout);

        return _MapEntry(entry);
    }

    public Task RenewLockAsync(IQueueEntry<T> entry)
    {
        return _foundatio.RenewLockAsync(_MapEntry(entry));
    }

    public Task CompleteAsync(IQueueEntry<T> entry)
    {
        return _foundatio.CompleteAsync(_MapEntry(entry));
    }

    public Task AbandonAsync(IQueueEntry<T> entry)
    {
        return _foundatio.AbandonAsync(_MapEntry(entry));
    }

    public Task<IEnumerable<T>> GetDeadLetterItemsAsync(CancellationToken cancellationToken = default)
    {
        return _foundatio.GetDeadletterItemsAsync(cancellationToken);
    }

    public Task StartAsync(
        Func<IQueueEntry<T>, CancellationToken, Task> handler,
        bool autoComplete = false,
        CancellationToken cancellationToken = default
    )
    {
        return _foundatio.StartWorkingAsync(
            (entry, token) => handler(_MapEntry(entry), token),
            autoComplete,
            cancellationToken
        );
    }

    public async Task<QueueStats> GetQueueStatsAsync()
    {
        return _MapStats(await _foundatio.GetQueueStatsAsync());
    }

    public Task DeleteQueueAsync()
    {
        return _foundatio.DeleteQueueAsync();
    }

    public void AttachBehavior(IQueueBehavior<T> behavior)
    {
        _foundatio.AttachBehavior(new FoundatioBehaviorEntryAdapter<T>(behavior));
    }

    #region Mapping

    [return: NotNullIfNotNull(nameof(options))]
    private static Foundatio.Queues.QueueEntryOptions? _MapOptions(QueueEntryOptions? options)
    {
        return options is null
            ? null
            : new()
            {
                CorrelationId = options.CorrelationId,
                UniqueId = options.UniqueId,
                Properties = options.Properties,
                DeliveryDelay = options.DeliveryDelay,
            };
    }

    [return: NotNullIfNotNull(nameof(options))]
    private static QueueEntryOptions? _MapOptions(Foundatio.Queues.QueueEntryOptions? options)
    {
        return options is null
            ? null
            : new()
            {
                CorrelationId = options.CorrelationId,
                UniqueId = options.UniqueId,
                Properties = options.Properties,
                DeliveryDelay = options.DeliveryDelay,
            };
    }

    private static QueueStats _MapStats(Foundatio.Queues.QueueStats stats)
    {
        return new()
        {
            Queued = stats.Queued,
            Working = stats.Working,
            DeadLetter = stats.Deadletter,
            Enqueued = stats.Enqueued,
            Dequeued = stats.Dequeued,
            Completed = stats.Completed,
            Abandoned = stats.Abandoned,
            Errors = stats.Errors,
            Timeouts = stats.Timeouts,
        };
    }

    private static FrameworkQueueEntryAdapter<T> _MapEntry(Foundatio.Queues.IQueueEntry<T> entry)
    {
        return new(entry);
    }

    private static FoundatioQueueEntryAdapter<T> _MapEntry(IQueueEntry<T> entry)
    {
        return new(entry);
    }

    #endregion
}
