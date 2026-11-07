// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Dashboard;

/// <summary>
/// Reads one module's changes from its store every change-poll interval and hands them to the
/// <see cref="DashboardEventPublisher"/>, so a change made through any node reaches this node's streams.
/// </summary>
/// <remarks>
/// A failed read marks the module's views <c>Stale</c> once and skips doubling numbers of intervals, up to
/// <see cref="MaxSkippedIntervals"/>, before the next try with the same cursor; the first read that succeeds clears
/// <c>Stale</c>. The reader's timer starts when the reader is created, so its intervals count from the stream that
/// started it.
/// </remarks>
internal sealed class ChangeReader : IDisposable
{
    /// <summary>The most intervals a failing reader waits between tries.</summary>
    internal const int MaxSkippedIntervals = 15;

    private readonly string _moduleId;
    private readonly IDashboardChangeSource _source;
    private readonly DashboardEventPublisher _publisher;
    private readonly ILogger _logger;
    private readonly PeriodicTimer _timer;
#pragma warning disable CA2213 // False positive: Dispose hands it to a continuation that disposes it once the loop ends.
    private readonly CancellationTokenSource _stopping = new();
#pragma warning restore CA2213
    private readonly Task _loop;

    /// <summary>Starts reading the changes of <paramref name="moduleId"/>.</summary>
    public ChangeReader(
        string moduleId,
        IDashboardChangeSource source,
        DashboardEventPublisher publisher,
        TimeSpan interval,
        TimeProvider timeProvider,
        ILogger logger
    )
    {
        _moduleId = moduleId;
        _source = source;
        _publisher = publisher;
        _logger = logger;
        _timer = new PeriodicTimer(interval, timeProvider);
        _loop = Task.Run(() => _RunAsync(_stopping.Token));
    }

    /// <summary>
    /// Stops the reader without waiting for a read in flight; the source sees its token cancelled.
    /// </summary>
    public void Dispose()
    {
        _stopping.Cancel();
        _timer.Dispose();
        _publisher.ForgetStale(_moduleId);

        // The loop ends on its own once cancelled; the token source outlives it until the loop finishes.
        _ = _loop.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            _stopping,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private async Task _RunAsync(CancellationToken cancellationToken)
    {
        string? cursor = null;
        var failures = 0;
        var skip = 0;
        var first = true;
        var succeeded = false;

        try
        {
            while (true)
            {
                // The start cursor is read at once, so the first read covers changes since the stream opened.
                if (!first && !await _timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                first = false;

                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                try
                {
                    if (cursor is null)
                    {
                        cursor = await _source.GetStartCursorAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var batch = await _source.ReadChangesAsync(cursor, cancellationToken).ConfigureAwait(false);

                        foreach (var change in batch.Changes)
                        {
                            _publisher.Accept(change);
                        }

                        cursor = batch.Cursor;
                    }

                    // A reader that stopped mid-failure may have left the module marked stale after the stop forgot it,
                    // so the first success of a new reader clears whatever it finds.
                    if (failures > 0 || (!succeeded && _publisher.IsStale(_moduleId)))
                    {
                        _publisher.ReportStale(_moduleId, stale: false);
                    }

                    failures = 0;
                    succeeded = true;
                }
                catch (Exception e) when (!cancellationToken.IsCancellationRequested)
                {
                    // This loop is the read's only caller: a failing store must not end the stream, so the failure is
                    // logged, the views are told the module is stale, and the same read is retried later.
                    failures++;
                    skip = (int)Math.Min(MaxSkippedIntervals, (1L << Math.Min(failures, 30)) - 1);
                    _logger.ChangeReadFailed(e, _moduleId, skip + 1);

                    if (failures == 1)
                    {
                        _publisher.ReportStale(_moduleId, stale: true);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped: the node's last stream closed or the host is stopping.
        }
    }
}

internal static partial class ChangeReaderLog
{
    [LoggerMessage(
        EventId = 10,
        EventName = "DashboardChangeReadFailed",
        Level = LogLevel.Warning,
        Message = "Dashboard module {ModuleId} failed to read its changes; its live views show stale, and the read is "
            + "retried after {Intervals} change-poll intervals"
    )]
    internal static partial void ChangeReadFailed(
        this ILogger logger,
        Exception exception,
        string moduleId,
        int intervals
    );
}
