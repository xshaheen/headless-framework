// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Checks;
using Headless.Dashboard;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs;

/// <summary>
/// Publishes the Jobs rows this node changed to its own dashboard streams: it reads each row back by id after the
/// write committed and publishes the row's slim projection with its change marker as the version.
/// </summary>
/// <remarks>
/// The stamping writes return only a row count, so the marker has to be read back. The job managers notify from
/// inside their claim and execution paths, so those notifications queue here and one background reader does the reads:
/// a slow store never holds a claim loop. The queue is bounded and drops its oldest entry when full, because a row it
/// drops still reaches every stream through the change reader.
/// </remarks>
internal sealed class JobsChangePublisher : IAsyncDisposable
{
    private const int _QueueCapacity = 10_000;

    private readonly IJobsDashboardBackend _backend;
    private readonly IDashboardEventPublisher _publisher;
    private readonly ILogger<JobsChangePublisher> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<(JobChangeKind Kind, Guid Id, bool AffectsAttention)> _queue;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _startLock = new();
    private Task? _reader;

    public JobsChangePublisher(
        IJobsDashboardBackend backend,
        IDashboardEventPublisher publisher,
        TimeProvider timeProvider,
        ILogger<JobsChangePublisher> logger
    )
    {
        _timeProvider = Argument.IsNotNull(timeProvider);
        _backend = Argument.IsNotNull(backend);
        _publisher = Argument.IsNotNull(publisher);
        _logger = Argument.IsNotNull(logger);
        _queue = Channel.CreateBounded<(JobChangeKind, Guid, bool)>(
            new BoundedChannelOptions(_QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            }
        );
    }

    /// <summary>Queues a row a job manager wrote, to be read back and published by the background reader.</summary>
    public void Enqueue(JobChangeKind kind, Guid id, bool affectsAttention)
    {
        if (_queue.Writer.TryWrite((kind, id, affectsAttention)))
        {
            _EnsureReader();
        }
    }

    /// <summary>Publishes that a row is gone, so open views refetch it and show it no longer exists.</summary>
    public void PublishRemoved(JobChangeKind kind, Guid id)
    {
        _publisher.Publish(
            JobsViews.Removed(
                JobsDashboardOptions.ModuleId,
                kind == JobChangeKind.CronJob ? JobsViews.CronJobKind : JobsViews.RunKind,
                id
            )
        );
    }

    /// <summary>Reads a row back now and publishes it, for a write this module made itself.</summary>
    public async Task PublishAsync(
        JobChangeKind kind,
        Guid id,
        bool affectsAttention,
        CancellationToken cancellationToken
    )
    {
        if (kind == JobChangeKind.CronJob)
        {
            var cronJob = await _backend.GetCronJobAsync(id, cancellationToken).ConfigureAwait(false);

            if (cronJob is null)
            {
                PublishRemoved(kind, id);

                return;
            }

            var preview = _backend.Preview(
                cronJob.Expression,
                cronJob.TimeZoneId,
                _timeProvider.GetUtcNow().UtcDateTime,
                count: 1
            );
            _publisher.Publish(JobsViews.Change(JobsDashboardOptions.ModuleId, cronJob, preview));

            return;
        }

        var run = await _backend.GetRunAsync(id, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            PublishRemoved(kind, id);

            return;
        }

        _publisher.Publish(JobsViews.Change(JobsDashboardOptions.ModuleId, run, affectsAttention));
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

    // Started on the first notification, not at host start: a node whose Jobs managers never write starts no reader.
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
            var (kind, id, affectsAttention) in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)
        )
        {
            try
            {
                await PublishAsync(kind, id, affectsAttention, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // The reader serves every later notification, so one failed read is logged and skipped.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _logger.LogJobsChangePublishFailed(exception, kind, id);
            }
        }
    }
}

internal static partial class JobsChangePublisherLogs
{
    [LoggerMessage(
        EventId = 2030,
        EventName = "JobsDashboardChangePublishFailed",
        Level = LogLevel.Warning,
        Message = "Jobs dashboard could not read back {Kind} {Id} to publish it; the change reader still delivers it."
    )]
    internal static partial void LogJobsChangePublishFailed(
        this ILogger logger,
        Exception exception,
        JobChangeKind kind,
        Guid id
    );
}
