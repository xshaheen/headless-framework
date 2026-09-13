// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Checks;
using Headless.Jobs.Internal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.BackgroundServices;

/// <summary>
/// Hosted worker that runs the post-commit side effects of coordinated job writes off the commit path. The commit
/// callback only <see cref="TrySignal" />s; this loop acquires immediately due jobs, arms the scheduler wake, and
/// notifies the dashboard, so a hung dispatcher or notification hub can never hold a caller's commit, DI scope, or
/// database connection.
/// </summary>
/// <remarks>
/// <para>
/// Registered whenever Jobs is registered, including with <c>DisableBackgroundServices()</c>: on such hosts the
/// dispatcher and scheduler are no-ops and the notification send reaches a dashboard only when one is mounted, so
/// the worker is harmless and keeps the callback shape uniform. Post-commit acceleration therefore requires a running
/// host; before the host starts (or after it stops) a signal is queued or dropped, and the scheduler's poll sweep
/// picks the committed row up on its next pass.
/// </para>
/// <para>
/// The channel is bounded and never blocks a producer: a full channel drops the incoming signal with a warning and a
/// counter (<c>headless.jobs.post_commit_signals.dropped</c>). Each signal is bounded by <see cref="SignalDeadline" />
/// so one hung send cannot starve later signals; a fault or timeout is logged and the loop continues. On stop the
/// worker rejects new signals and drains the ones already queued within the host's shutdown budget.
/// </para>
/// </remarks>
internal sealed partial class JobsPostCommitSignalService(
    JobsActivationBarrier activationBarrier,
    TimeProvider timeProvider,
    ILogger<JobsPostCommitSignalService> logger
) : BackgroundService
{
    /// <summary>
    /// Bounded queue depth. Sized like the messaging dispatcher's publish channel: deep enough to absorb a burst of
    /// commits while a dashboard send is slow, shallow enough that a wedged worker cannot pin unbounded memory.
    /// </summary>
    internal const int Capacity = 1024;

    /// <summary>
    /// Per-signal bound. Matches the scheduler's fallback poll interval: a signal that has not finished by the time
    /// the sweep would have found the row anyway has nothing left to accelerate.
    /// </summary>
    internal static readonly TimeSpan SignalDeadline = TimeSpan.FromSeconds(30);

    private readonly Channel<JobsPostCommitSignal> _channel = Channel.CreateBounded<JobsPostCommitSignal>(
        new BoundedChannelOptions(Capacity)
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            // Commit callbacks from any request thread write concurrently.
            SingleWriter = false,
            // Wait mode makes TryWrite report false when full instead of overwriting queued work.
            FullMode = BoundedChannelFullMode.Wait,
        }
    );

    // Cancelled only when the host's shutdown budget is exhausted — NOT by the stopping token, which base.StopAsync
    // cancels immediately and would abort the drain of signals that are already queued.
    private readonly CancellationTokenSource _drainCts = new();
    private readonly JobsActivationBarrier _activationBarrier = Argument.IsNotNull(activationBarrier);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
    private readonly ILogger<JobsPostCommitSignalService> _logger = Argument.IsNotNull(logger);
    private int _stopping;
    private int _disposed;

    /// <summary>Signals queued and not yet picked up by the worker.</summary>
    internal int PendingCount => _channel.Reader.Count;

    /// <summary>
    /// Queues a signal without waiting. Returns <see langword="false" /> when the signal was dropped because the
    /// channel is full or the worker is stopping; the committed row is then recovered by the poll sweep.
    /// </summary>
    public bool TrySignal(JobsPostCommitSignal signal)
    {
        Argument.IsNotNull(signal);

        if (_channel.Writer.TryWrite(signal))
        {
            return true;
        }

        var reason = Volatile.Read(ref _stopping) != 0 ? "stopping" : "full";
        JobsMetrics.PostCommitSignalDropped(reason);
        Log.PostCommitSignalDropped(_logger, signal.JobScope, reason, _channel.Reader.Count);

        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Activation gate: side effects select and claim job rows, so they wait for the same fingerprint drain the
        // scheduler waits for. Signals queued meanwhile stay in the channel.
        Exception? activationFailure;

        try
        {
            activationFailure = await _activationBarrier.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (activationFailure is not null)
        {
            // The failure already aborted host startup through the initializer; stay closed rather than acquire rows
            // under an unverified schedule interpretation.
            Log.StoppedOnActivationFailure(_logger, activationFailure);

            return;
        }

        var reader = _channel.Reader;

        try
        {
            // StopAsync completes the writer BEFORE base.StopAsync cancels stoppingToken, so a stop with queued
            // signals falls through to the inner loop and drains them; the wait observes the token only once the
            // queue is empty, or when the service is disposed without a stop.
            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var signal))
                {
                    if (_drainCts.IsCancellationRequested)
                    {
                        return;
                    }

                    await _ProcessAsync(signal).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopped while idle.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _stopping, 1);
        _channel.Writer.TryComplete();

        try
        {
            // base.StopAsync returns once the loop has drained or the host's shutdown budget has expired.
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // Budget exhausted: abandon the remaining signals and cancel the in-flight side effect cooperatively.
                // The poll sweep owns whatever is left.
                await _drainCts.CancelAsync().ConfigureAwait(false);
            }
        }
    }

    public override void Dispose()
    {
        // The host disposes this instance twice (once as the singleton, once as the hosted service).
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // A side effect abandoned at its deadline still holds the drain token; cancelling here stops it with the host.
        _drainCts.Cancel();
        _drainCts.Dispose();
        base.Dispose();
    }

    private async Task _ProcessAsync(JobsPostCommitSignal signal)
    {
        Task? work = null;

        try
        {
            // The deadline is a wait bound, not a cancellation: a slow send keeps running in the background while
            // the loop moves on to the next signal, and shutdown-budget exhaustion is the only cancellation the side
            // effects observe. That keeps one token per worker instead of one source per signal.
            work = signal.RunAsync(_timeProvider.GetUtcNow(), _drainCts.Token);
            await work.WaitAsync(SignalDeadline, _timeProvider, _drainCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_drainCts.IsCancellationRequested)
        {
            Log.PostCommitSignalAbandonedOnShutdown(_logger, signal.JobScope);
        }
        catch (TimeoutException) when (work is { IsCompleted: false })
        {
            // A side effect's own TimeoutException completes `work` and is reported as a failure below; only the wait
            // bound lands here.
            Log.PostCommitSignalTimedOut(_logger, signal.JobScope, SignalDeadline);
            LateFaultObserver.ObserveLateFault(work, _logger, signal.JobScope, Log.PostCommitSignalFailed);
        }
        catch (Exception e)
        {
            Log.PostCommitSignalFailed(_logger, signal.JobScope, e);
        }
    }

    // Surfaces a fault from an abandoned side effect instead of leaving it unobserved.
    private static partial class Log
    {
        [LoggerMessage(
            LogLevel.Warning,
            "Post-commit signal for {JobScope} was dropped ({Reason}; {Pending} pending). The job row is committed; "
                + "the scheduler's polling sweep picks it up on its next pass."
        )]
        public static partial void PostCommitSignalDropped(ILogger logger, string jobScope, string reason, int pending);

        [LoggerMessage(
            LogLevel.Warning,
            "Post-commit side effects failed for {JobScope}. The job row is committed; the scheduler's polling sweep "
                + "is the recovery path."
        )]
        public static partial void PostCommitSignalFailed(ILogger logger, string jobScope, Exception exception);

        [LoggerMessage(
            LogLevel.Warning,
            "Post-commit side effects for {JobScope} did not finish within {Deadline} and were abandoned. The job row "
                + "is committed; the scheduler's polling sweep is the recovery path."
        )]
        public static partial void PostCommitSignalTimedOut(ILogger logger, string jobScope, TimeSpan deadline);

        [LoggerMessage(
            LogLevel.Debug,
            "Post-commit side effects for {JobScope} were abandoned because the host shutdown budget was exhausted."
        )]
        public static partial void PostCommitSignalAbandonedOnShutdown(ILogger logger, string jobScope);

        [LoggerMessage(
            LogLevel.Warning,
            "The Jobs post-commit worker stays closed because activation failed; coordinated enqueues are recovered "
                + "by the scheduler's polling sweep once the host is healthy."
        )]
        public static partial void StoppedOnActivationFailure(ILogger logger, Exception exception);
    }
}
