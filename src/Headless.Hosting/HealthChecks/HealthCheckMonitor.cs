// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>Runs health checks on demand and keeps a bounded history of their results.</summary>
/// <param name="healthCheckService">Runs the checks.</param>
/// <param name="options">The registrations, read on every call so a removed check disappears.</param>
/// <param name="timeProvider">Stamps each run's start and each result's completion.</param>
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
    private readonly Dictionary<string, CheckHistory> _history = new(_NameComparer);
    private SharedRun? _inFlight;
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
            run = _inFlight ??= new SharedRun(CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token));
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

        lock (_gate)
        {
            Ensure.NotDisposed(_disposed, this);
            lifetime = _lifetime.Token;
        }

        if (!options.Value.Registrations.Any(registration => _NameComparer.Equals(registration.Name, name)))
        {
            return null;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
        var startedAt = timeProvider.GetUtcNow();
        var report = await healthCheckService
            .CheckHealthAsync(registration => _NameComparer.Equals(registration.Name, name), linked.Token)
            .ConfigureAwait(false);

        if (!linked.IsCancellationRequested)
        {
            _Record(report, startedAt);
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
        var startedAt = timeProvider.GetUtcNow();

        try
        {
            // Forced yielding keeps the run's continuation off the thread that cancels it, so a cancel never runs
            // the cleanup below, which disposes the token source, inside its own callbacks.
            var report = await healthCheckService
                .CheckHealthAsync(run.Cancellation.Token)
                .ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

            if (run.Cancellation.IsCancellationRequested)
            {
                run.Completion.TrySetCanceled(run.Cancellation.Token);

                return;
            }

            _Record(report, startedAt);
            run.Completion.TrySetResult(report);
        }
        catch (OperationCanceledException exception)
        {
            run.Completion.TrySetCanceled(exception.CancellationToken);
        }
#pragma warning disable CA1031 // False positive: the failure is not swallowed; every caller waiting on the shared run receives it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            run.Completion.TrySetException(exception);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_inFlight, run))
                {
                    _inFlight = null;
                }

                // Under the lock, so no waiter cancels it while it is disposed.
                run.Cancellation.Dispose();
                run.Disposed = true;
            }
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
            // the shared run, and every later caller, forever.
            if (ReferenceEquals(_inFlight, run))
            {
                _inFlight = null;
            }

            run.Cancellation.Cancel();
        }
    }

    private void _Record(HealthReport report, DateTimeOffset startedAt)
    {
        var completedAt = timeProvider.GetUtcNow();

        lock (_gate)
        {
            foreach (var (name, entry) in report.Entries)
            {
                if (!_history.TryGetValue(name, out var history))
                {
                    _history[name] = history = new CheckHistory();
                }

                // A run that started before the latest recorded one finished after it: its result is older news.
                if (history.LatestStartedAt is { } latest && startedAt < latest)
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
                    new HealthCheckProbeResult(entry.Status, entry.Duration, description, completedAt)
                );
                history.LatestStartedAt = startedAt;
            }
        }
    }

    private HealthCheckDescriptor _Describe(HealthCheckRegistration registration)
    {
        return new HealthCheckDescriptor(
            registration.Name,
            [.. registration.Tags.Order(StringComparer.Ordinal)],
            registration.FailureStatus,
            registration.Timeout == Timeout.InfiniteTimeSpan ? null : registration.Timeout,
            registration.Tags.Contains(aliveTag)
        );
    }

    private sealed class CheckHistory
    {
        public Queue<HealthCheckProbeResult> Results { get; } = new(_HistoryDepth);

        public DateTimeOffset? LatestStartedAt { get; set; }
    }

    private sealed class SharedRun(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;

        public TaskCompletionSource<HealthReport> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Waiters { get; set; }

        public bool Disposed { get; set; }
    }
}
