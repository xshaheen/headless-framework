// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Jobs;

/// <summary>
/// Coalesces one execution's progress reports into throttled store writes: the first report is written at once, then
/// at most one write per interval carries the latest value. The writer starts on the first report, so a job that never
/// reports costs nothing, and it lives for the whole claim, in-process retries and their backoff included.
/// </summary>
internal sealed class JobProgressReporter(
    JobExecutionState context,
    IInternalJobManager internalJobManager,
    TimeProvider timeProvider,
    TimeSpan interval,
    Func<bool> ownsExecution,
    ILogger logger
) : IJobProgressSink, IAsyncDisposable
{
    private readonly Lock _syncRoot = new();
    private readonly CancellationTokenSource _stopCts = new();
    private TaskCompletionSource _reported = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private JobProgress? _pending;
    private Task? _writer;
    private bool _stopped;

    public void Report(JobProgress progress)
    {
        lock (_syncRoot)
        {
            // A handler that ignores cancellation can keep reporting after the run ended; the terminal write already
            // carried the last value, so a late report has nowhere to go.
            if (_stopped)
            {
                return;
            }

            _pending = progress;
            _reported.TrySetResult();
            // Task.Run keeps the writer's first store round-trip off the reporting thread and out of this lock.
            if (_writer is null)
            {
                var stopToken = _stopCts.Token;
                _writer = Task.Run(() => _WriteLoopAsync(stopToken), CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Stops the writer and returns the latest report not known to be stored, for the caller to persist with the run's
    /// terminal status; <see langword="null"/> when every report was written.
    /// </summary>
    public async Task<JobProgress?> StopAsync()
    {
        Task? writer;
        lock (_syncRoot)
        {
            if (_stopped)
            {
                return null;
            }

            _stopped = true;
            writer = _writer;
        }

        await _stopCts.CancelAsync().ConfigureAwait(false);
        if (writer is not null)
        {
            // The loop handles its own faults and stops on cancellation, so awaiting it only bounds its lifetime.
            await writer.ConfigureAwait(false);
        }

        _stopCts.Dispose();

        lock (_syncRoot)
        {
            var pending = _pending;
            _pending = null;
            return pending;
        }
    }

    /// <summary>Stops the writer, dropping any unwritten report; a no-op once <see cref="StopAsync"/> ran.</summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task _WriteLoopAsync(CancellationToken stopToken)
    {
        try
        {
            while (true)
            {
                Task reported;
                lock (_syncRoot)
                {
                    reported = _reported.Task;
                }

                await reported.WaitAsync(stopToken).ConfigureAwait(false);

                JobProgress? progress;
                lock (_syncRoot)
                {
                    progress = _pending;
                    _pending = null;
                    _reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                if (progress is not { } latest)
                {
                    continue;
                }

                // After lease loss a handler that ignores its token can keep reporting, and the ownership fence alone
                // would still pass once this node re-claims the row for a new run. Lost ownership is permanent for this
                // execution, so stop writing instead of overwriting the next run's progress.
                if (!ownsExecution())
                {
                    return;
                }

                await _WriteAsync(latest, stopToken).ConfigureAwait(false);

                // The throttle: reports arriving during this wait coalesce into the next write, last value wins.
                await timeProvider.Delay(interval, stopToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // StopAsync ended the run's reporting.
        }
    }

    private async Task _WriteAsync(JobProgress progress, CancellationToken stopToken)
    {
        try
        {
            var written = await internalJobManager
                .UpdateProgressAsync(context, progress, stopToken)
                .ConfigureAwait(false);

            if (!written)
            {
                // The ownership fence matched nothing: the lease was lost or membership is unestablished. Lease loss
                // belongs to the renewal loop, so progress only notes it and drops the value.
                logger.LogJobProgressWriteFenced(context.JobId, context.FunctionName);
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // The interrupted write may or may not have committed. Hand the value back so the terminal write stores it
            // again; rewriting the same value is harmless.
            _Requeue(progress, signal: false);
            throw;
        }
        catch (Exception exception)
        {
            // Progress is advisory: a failed write must not fail the run. Retry with the latest value after the wait.
            logger.LogJobProgressWriteFailed(exception, context.JobId, context.FunctionName);
            _Requeue(progress, signal: true);
        }
    }

    private void _Requeue(JobProgress progress, bool signal)
    {
        lock (_syncRoot)
        {
            // A newer report that arrived during the write supersedes the one that failed.
            _pending ??= progress;
            if (signal)
            {
                _reported.TrySetResult();
            }
        }
    }
}

internal static partial class JobProgressReporterLog
{
    [LoggerMessage(
        EventId = 3116,
        Level = LogLevel.Warning,
        Message = "Progress write for job {JobId} ({Function}) failed; the job keeps running and the latest progress is "
            + "retried on the next write."
    )]
    public static partial void LogJobProgressWriteFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId,
        string function
    );

    [LoggerMessage(
        EventId = 3117,
        Level = LogLevel.Debug,
        Message = "Progress write for job {JobId} ({Function}) matched no owned running row; the value was dropped."
    )]
    public static partial void LogJobProgressWriteFenced(this ILogger logger, Guid jobId, string function);
}
