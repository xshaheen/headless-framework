// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>Runs health checks on demand and keeps a bounded history of their results.</summary>
/// <param name="healthCheckService">Runs the checks.</param>
/// <param name="options">The registrations, read on every call so a removed check disappears.</param>
/// <param name="timeProvider">Stamps each result's completion.</param>
/// <param name="aliveTag">The tag that marks a liveness check.</param>
internal sealed class HealthCheckMonitor(
    HealthCheckService healthCheckService,
    IOptions<HealthCheckServiceOptions> options,
    TimeProvider timeProvider,
    string aliveTag
) : IHealthCheckMonitor, IDisposable
{
    private const int _HistoryDepth = 30;

    // The health check service keys registrations and report entries case-insensitively, so the history does too.
    private static readonly StringComparer _NameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, CheckHistory> _history = [with(_NameComparer)];
    private SharedRun? _inFlight;
    private long _lastRunNumber;
    private bool _disposed;

    public IReadOnlyList<HealthCheckDescriptor> GetChecks()
    {
        return [.. options.Value.Registrations.Select(_Describe)];
    }

    public async Task<HealthReport> ProbeAllAsync(CancellationToken cancellationToken = default)
    {
        SharedRun run;
        bool started;

        lock (_gate)
        {
            Ensure.NotDisposed(_disposed, this);

            started = _inFlight is null;
#pragma warning disable CA2000 // False positive: the run owns the linked source and disposes it when the run ends.
            run = _inFlight ??= new SharedRun(
                CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token),
                ++_lastRunNumber
            );
#pragma warning restore CA2000
            run.Waiters++;
        }

        if (started)
        {
            _ = _RunSharedAsync(run);
        }

        try
        {
            return await run.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _Leave(run);

            throw;
        }
    }

    public async Task<HealthReport?> ProbeAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        CancellationToken lifetime;
        long runNumber;

        lock (_gate)
        {
            Ensure.NotDisposed(_disposed, this);
            lifetime = _lifetime.Token;
            runNumber = ++_lastRunNumber;
        }

        if (!options.Value.Registrations.Any(registration => _NameComparer.Equals(registration.Name, name)))
        {
            return null;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
        var report = await healthCheckService
            .CheckHealthAsync(registration => _NameComparer.Equals(registration.Name, name), linked.Token)
            .ConfigureAwait(false);

        if (!linked.IsCancellationRequested)
        {
            _Record(report, runNumber, sharedRun: null);
        }

        return report;
    }

    public HealthCheckSnapshot GetSnapshot()
    {
        var descriptors = GetChecks();
        var checks = new HealthCheckHistory[descriptors.Count];
        HealthStatus? worst = null;

        lock (_gate)
        {
            for (var i = 0; i < descriptors.Count; i++)
            {
                HealthCheckProbeResult[] results = _history.TryGetValue(descriptors[i].Name, out var history)
                    ? [.. history.Results]
                    : [];

                if (results.Length > 0 && (worst is null || results[^1].Status < worst))
                {
                    worst = results[^1].Status;
                }

                checks[i] = new HealthCheckHistory(descriptors[i], results);
            }
        }

        return new HealthCheckSnapshot(worst, checks);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async Task _RunSharedAsync(SharedRun run)
    {
        var token = run.Cancellation.Token;
        HealthReport? report = null;
        Exception? failure = null;

        try
        {
            // Forced yielding keeps the run's continuation off the thread that cancels it, so a cancel never runs
            // the cleanup below, which disposes the token source, inside its own callbacks.
            report = await healthCheckService
                .CheckHealthAsync(token)
                .ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

            if (!_Record(report, run.Number, run))
            {
                report = null;
            }
        }
        catch (OperationCanceledException exception)
        {
            token = exception.CancellationToken;
        }
#pragma warning disable CA1031 // False positive: the failure is not swallowed; every caller waiting on the shared run receives it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            failure = exception;
        }

        lock (_gate)
        {
            // Before any waiter resumes, so a caller that retries at once starts a new run instead of joining this one.
            if (ReferenceEquals(_inFlight, run))
            {
                _inFlight = null;
            }

            // The gate orders this against _Leave's abandon decision: a waiter that leaves after this point sees
            // Disposed and cancels nothing. A cancel _Leave already decided runs outside the lock and may land on the
            // disposed source, which _Leave tolerates.
            run.Cancellation.Dispose();
            run.Disposed = true;
        }

        if (failure is not null)
        {
            run.Completion.TrySetException(failure);
        }
        else if (report is not null)
        {
            run.Completion.TrySetResult(report);
        }
        else
        {
            run.Completion.TrySetCanceled(token);
        }
    }

    private void _Leave(SharedRun run)
    {
        lock (_gate)
        {
            if (run.Disposed || --run.Waiters > 0)
            {
                return;
            }

            // Nobody waits any more: a registration timeout defaults to infinite, so a hung check would otherwise hold
            // the shared run, and every later caller, forever. Abandoning under the lock makes _Record's decision
            // atomic with this one, whenever the cancellation below lands.
            run.Abandoned = true;

            if (ReferenceEquals(_inFlight, run))
            {
                _inFlight = null;
            }
        }

        // Outside the lock: Cancel runs every check's cancellation callbacks synchronously, and none of them may hold
        // the monitor gate that GetSnapshot and every other probe take.
        try
        {
            run.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run finished and disposed its source between the decision above and this call: nothing to cancel.
        }
    }

    /// <summary>Records a run's results unless the shared run was abandoned or cancelled.</summary>
    /// <returns><see langword="false"/> when <paramref name="sharedRun"/> was abandoned or cancelled.</returns>
    private bool _Record(HealthReport report, long runNumber, SharedRun? sharedRun)
    {
        var completedAt = timeProvider.GetUtcNow();

        lock (_gate)
        {
            if (sharedRun is { Abandoned: true } || sharedRun?.Cancellation.IsCancellationRequested == true)
            {
                return false;
            }

            foreach (var (name, entry) in report.Entries)
            {
                if (!_history.TryGetValue(name, out var history))
                {
                    _history[name] = history = new CheckHistory();
                }

                // A run that started before the latest recorded one finished after it: its result is older news. Run
                // numbers, not clock readings, order the runs, because two runs can start at one instant or across a
                // clock step.
                if (runNumber < history.LatestRunNumber)
                {
                    continue;
                }

                if (history.Results.Count == _HistoryDepth)
                {
                    history.Results.Dequeue();
                }

                // A thrown exception's message can carry host names or credentials, and the history may be served
                // to an anonymous dashboard, so a failure that carries one keeps only fixed text.
                var description = entry.Exception is null ? entry.Description : $"The '{name}' health check failed.";

                history.Results.Enqueue(
                    new HealthCheckProbeResult
                    {
                        Status = entry.Status,
                        Duration = entry.Duration,
                        Description = description,
                        CompletedAt = completedAt,
                    }
                );
                history.LatestRunNumber = runNumber;
            }
        }

        return true;
    }

    private HealthCheckDescriptor _Describe(HealthCheckRegistration registration)
    {
        return new HealthCheckDescriptor
        {
            Name = registration.Name,
            Tags = [.. registration.Tags.Order(StringComparer.Ordinal)],
            FailureStatus = registration.FailureStatus,
            Timeout = registration.Timeout == Timeout.InfiniteTimeSpan ? null : registration.Timeout,
            IsLiveness = registration.Tags.Contains(aliveTag),
        };
    }

    private sealed class CheckHistory
    {
        public Queue<HealthCheckProbeResult> Results { get; } = new(_HistoryDepth);

        public long LatestRunNumber { get; set; }
    }

    private sealed class SharedRun(CancellationTokenSource cancellation, long number)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;

        public long Number { get; } = number;

        public bool Abandoned { get; set; }

        public TaskCompletionSource<HealthReport> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Waiters { get; set; }

        public bool Disposed { get; set; }
    }
}
