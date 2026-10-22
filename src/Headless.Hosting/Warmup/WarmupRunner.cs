// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Hosting;

/// <summary>
/// Runs every registered <see cref="IWarmup" /> concurrently in the background once the host has started, retrying each
/// failure until it succeeds or the host stops, and reports whether the warm-ups that gate readiness have finished.
/// </summary>
/// <remarks>
/// It never throws out of the background run: a faulted background task would read as a crashed host, and a warm-up
/// failure only delays readiness. As an <see cref="IInitializer" /> it lets a test server wait until the host is warm.
/// </remarks>
internal sealed partial class WarmupRunner(IServiceScopeFactory scopes, ILogger<WarmupRunner> logger)
    : IHostedLifecycleService,
        IInitializer,
        IDisposable
{
    private readonly ConcurrentDictionary<string, string?> _pending = new(StringComparer.Ordinal);
    private volatile TaskCompletionSource _gated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _stopping;
    private Task? _run;

    public bool IsInitialized => _gated.Task.IsCompletedSuccessfully;

    public Task WaitForInitializationAsync(CancellationToken cancellationToken = default) =>
        _gated.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Names each gating warm-up that has not finished and its last error, or returns <see langword="null" /> when the
    /// host is warm.
    /// </summary>
    public string? DescribePending()
    {
        if (IsInitialized)
        {
            return null;
        }

        if (_pending.IsEmpty)
        {
            return "Warm-up has not started.";
        }

        var steps = _pending
            .OrderBy(static p => p.Key, StringComparer.Ordinal)
            .Select(static p => p.Value is null ? p.Key : $"{p.Key} (last error: {p.Value})");

        return "Warming up: " + string.Join("; ", steps);
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        // A restarted host warms up again, so it must not report the earlier start's completion.
        if (_gated.Task.IsCompleted)
        {
            _gated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _pending.Clear();

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        _stopping = new CancellationTokenSource();
        var scope = scopes.CreateAsyncScope();

        // Resolved here, not in the background run, so the readiness check names every pending warm-up from the first
        // probe after the start returns.
        if (!_TryResolve(scope.ServiceProvider, out var warmups))
        {
            _run = scope.DisposeAsync().AsTask();

            return Task.CompletedTask;
        }

        var gating = warmups.Where(static w => w.GatesReadiness).ToList();

        foreach (var warmup in gating)
        {
            _pending[warmup.Name] = null;
        }

        if (gating.Count == 0)
        {
            _gated.TrySetResult();
        }

        var token = _stopping.Token;
        _run = Task.Run(() => _RunAllAsync(scope, warmups, token), CancellationToken.None);

        return Task.CompletedTask;
    }

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        if (_stopping is not null)
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_run is not null)
        {
            await _run.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        _stopping?.Dispose();
        _stopping = null;
        _run = null;

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _stopping?.Dispose();
        _stopping = null;
    }

    private bool _TryResolve(IServiceProvider services, out IReadOnlyList<IWarmup> warmups)
    {
#pragma warning disable CA1031 // False positive: a warm-up that cannot be constructed only keeps the host unready; the host must not crash.
        try
        {
            warmups = [.. services.GetServices<IWarmup>()];

            return true;
        }
        catch (Exception exception)
        {
            _pending["(construction)"] = exception.Message;
            LogWarmupFailed(logger, exception, "(construction)", 1);
            warmups = [];

            return false;
        }
#pragma warning restore CA1031
    }

    private async Task _RunAllAsync(
        AsyncServiceScope scope,
        IReadOnlyList<IWarmup> warmups,
        CancellationToken cancellationToken
    )
    {
        await using (scope.ConfigureAwait(false))
        {
            await Task.WhenAll(warmups.Select(w => _RunOneAsync(w, cancellationToken))).ConfigureAwait(false);
        }
    }

    private async Task _RunOneAsync(IWarmup warmup, CancellationToken cancellationToken)
    {
        // Real time on purpose: the back-off paces retries against external systems, not an app schedule.
        var backoff = new ReconnectBackoff();
        var started = Stopwatch.GetTimestamp();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await warmup.WarmUpAsync(cancellationToken).ConfigureAwait(false);
                var elapsed = Stopwatch.GetElapsedTime(started);
                LogWarmupCompleted(logger, warmup.Name, attempt, elapsed.TotalMilliseconds);
                _Complete(warmup);

                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // False positive: a failed warm-up is retried and reported by the readiness check; the host must not crash.
            catch (Exception exception)
            {
                if (warmup.GatesReadiness)
                {
                    _pending[warmup.Name] = exception.Message;
                }

                LogWarmupFailed(logger, exception, warmup.Name, attempt);
            }
#pragma warning restore CA1031

            if (!await backoff.WaitAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private void _Complete(IWarmup warmup)
    {
        if (!warmup.GatesReadiness)
        {
            return;
        }

        _pending.TryRemove(warmup.Name, out _);

        if (_pending.IsEmpty)
        {
            _gated.TrySetResult();
        }
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "WarmupCompleted",
        Level = LogLevel.Information,
        Message = "Warm-up {Warmup} completed on attempt {Attempt} after {ElapsedMs:F0} ms"
    )]
    private static partial void LogWarmupCompleted(ILogger logger, string warmup, int attempt, double elapsedMs);

    [LoggerMessage(
        EventId = 2,
        EventName = "WarmupFailed",
        Level = LogLevel.Warning,
        Message = "Warm-up {Warmup} failed on attempt {Attempt} and is retried"
    )]
    private static partial void LogWarmupFailed(ILogger logger, Exception exception, string warmup, int attempt);
}
