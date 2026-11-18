// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.DistributedLocks;
using Tests.Oracle;

namespace Tests.Scenarios;

/// <summary>How a <see cref="LockHolderWorkload"/> run is sized.</summary>
public sealed record LockHolderWorkloadOptions(
    int Workers,
    IReadOnlyList<string> Resources,
    TimeSpan Duration,
    TimeSpan MaxHold,
    DistributedLockAcquireOptions AcquireOptions
);

/// <summary>What the workers saw besides the ledger: failed calls the faults caused.</summary>
public sealed record LockHolderWorkloadCounts(
    int AcquireTimeouts,
    int AcquireErrors,
    int ReleaseErrors,
    string? LastReleaseError
)
{
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"acquire timeouts {AcquireTimeouts}, acquire errors {AcquireErrors}, release errors {ReleaseErrors}{(LastReleaseError is null ? string.Empty : $" (last {LastReleaseError})")}"
        );
}

/// <summary>
/// K workers that loop acquire, hold briefly, write to the protected resource fenced by the grant's token, then
/// release, spread over a few resource names and over the given lock nodes (one per simulated process). The write comes
/// after the hold, so a holder whose session died during the hold writes late, as a stale holder would.
/// </summary>
public sealed class LockHolderWorkload(
    IReadOnlyList<IDistributedLock> nodes,
    FencingLedger ledger,
    FencedResourceStore store,
    int seed
)
{
    private static readonly TimeSpan _ErrorBackoff = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan _ReleaseBound = TimeSpan.FromSeconds(10);

    private int _acquireTimeouts;
    private int _acquireErrors;
    private int _releaseErrors;
    private string? _lastReleaseError;

    /// <summary>
    /// Runs the workers and <paramref name="faults"/> together for <see cref="LockHolderWorkloadOptions.Duration"/>,
    /// then stops both and waits for every worker to release.
    /// </summary>
    public async Task<LockHolderWorkloadCounts> RunAsync(
        LockHolderWorkloadOptions options,
        Func<FencingLedger, CancellationToken, Task> faults,
        CancellationToken cancellationToken
    )
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stop.CancelAfter(options.Duration);

        var workers = Enumerable
            .Range(0, options.Workers)
            .Select(index => _WorkAsync(index, options, stop.Token))
            .ToList();
        var faulting = _IgnoreStopAsync(faults(ledger, stop.Token), stop.Token);

        await Task.WhenAll([.. workers, faulting]).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return new LockHolderWorkloadCounts(_acquireTimeouts, _acquireErrors, _releaseErrors, _lastReleaseError);
    }

    private async Task _WorkAsync(int index, LockHolderWorkloadOptions options, CancellationToken stop)
    {
        var worker = string.Create(CultureInfo.InvariantCulture, $"w{index}");
        var node = nodes[index % nodes.Count];
        var random = new Random(seed + index);

        // Yield first, so every worker starts on the pool instead of the first ones running inline.
        await Task.Yield();

        while (!stop.IsCancellationRequested)
        {
            var resource = options.Resources[random.Next(options.Resources.Count)];
            var highWaterMark = store.HighWaterMark(resource);
            IDistributedLease? lease;

            try
            {
                lease = await node.TryAcquireAsync(resource, options.AcquireOptions, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is not OperationCanceledException || !stop.IsCancellationRequested)
            {
                // A fault in flight (a killed session, a cut route) fails the call; the worker tries again.
                Interlocked.Increment(ref _acquireErrors);
                await _PauseAsync(_ErrorBackoff, stop).ConfigureAwait(false);
                continue;
            }

            if (lease is null)
            {
                Interlocked.Increment(ref _acquireTimeouts);
                continue;
            }

            var token =
                lease.FencingToken
                ?? throw new InvalidOperationException(
                    $"The lock granted {resource} without a fencing token; the scenario needs a fencing backend."
                );

            ledger.Granted(worker, resource, token.Value, highWaterMark);

            try
            {
                // The hold is the window in which a fault can end the session while this worker still believes it
                // holds the lock. It is not cancelled by the stop, so the last holds still write late.
                await _PauseAsync(
                        TimeSpan.FromTicks((long)(options.MaxHold.Ticks * random.NextDouble())),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                store.TryWrite(worker, resource, token);
            }
            finally
            {
                ledger.Released(worker, resource, token.Value);
                await _ReleaseAsync(lease).ConfigureAwait(false);
            }
        }
    }

    private async Task _ReleaseAsync(IDistributedLease lease)
    {
        try
        {
            // A release on a session a fault killed can stall; bound it so one stuck release does not stop a worker.
            await lease.ReleaseAsync().WaitAsync(_ReleaseBound).ConfigureAwait(false);
            await lease.DisposeAsync().AsTask().WaitAsync(_ReleaseBound).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A release that fails under an injected fault is expected and only counted.
        catch (Exception e)
#pragma warning restore CA1031
        {
            Interlocked.Increment(ref _releaseErrors);
            _lastReleaseError = e.GetType().Name;
        }
    }

    private static async Task _PauseAsync(TimeSpan length, CancellationToken stop)
    {
        try
        {
            // real-wait: a lock holder holds for real time against a real backend, and a failed call backs off.
            await Task.Delay(length, stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The run ended; the worker loop sees the stop.
        }
    }

    private static async Task _IgnoreStopAsync(Task task, CancellationToken stop)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The fault loop ends with the run.
        }
    }
}
