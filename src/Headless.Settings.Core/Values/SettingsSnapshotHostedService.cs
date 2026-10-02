// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Settings.Values;

/// <summary>
/// Loads every registered settings snapshot in the background once the host starts, then re-reads each on its backstop
/// interval.
/// </summary>
/// <remarks>
/// <para>
/// Host startup never waits on a snapshot: a slow or unreachable settings store must not hold back the rest of the host.
/// The first load is attempted as soon as the loop runs and retried with jittered exponential backoff, capped at the
/// snapshot's backstop interval, until it succeeds. A failed attempt logs a warning, not an error: names defined in the
/// dynamic store fail validation until that store's definitions are cached, which is a normal boot. A caller that needs the value before then awaits
/// <see cref="ISettingsSnapshot{T}.GetAsync"/>, which loads on demand and shares the same serialized load.
/// </para>
/// <para>
/// Each snapshot keeps its own jittered due time, so different intervals stay independent and replicas started together
/// do not read in lockstep.
/// </para>
/// </remarks>
internal sealed partial class SettingsSnapshotHostedService(
    IEnumerable<ISettingsSnapshotEntry> snapshots,
    TimeProvider timeProvider,
    ILogger<SettingsSnapshotHostedService> logger
) : BackgroundService
{
    /// <summary>The first retry delay after a failed first load; it doubles up to the snapshot's backstop interval.</summary>
    internal static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);

    private readonly ISettingsSnapshotEntry[] _snapshots = [.. snapshots];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_snapshots.Length == 0)
        {
            return;
        }

        var due = new DateTimeOffset[_snapshots.Length];
        var retryDelay = new TimeSpan[_snapshots.Length];
        var now = timeProvider.GetUtcNow();

        for (var i = 0; i < _snapshots.Length; i++)
        {
            due[i] = now;
            retryDelay[i] = InitialRetryDelay;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = due.Min() - timeProvider.GetUtcNow();

                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, timeProvider, stoppingToken).ConfigureAwait(false);
                }

                now = timeProvider.GetUtcNow();

                for (var i = 0; i < _snapshots.Length; i++)
                {
                    if (due[i] > now)
                    {
                        continue;
                    }

                    var snapshot = _snapshots[i];

                    if (snapshot.IsLoaded)
                    {
                        await _ReloadAsync(snapshot, stoppingToken).ConfigureAwait(false);
                        due[i] = timeProvider.GetUtcNow() + _Jittered(snapshot.Backstop);

                        continue;
                    }

                    var retryIn = _Jittered(retryDelay[i]);

                    if (await _TryLoadAsync(snapshot, retryIn, stoppingToken).ConfigureAwait(false))
                    {
                        due[i] = timeProvider.GetUtcNow() + _Jittered(snapshot.Backstop);
                    }
                    else
                    {
                        due[i] = timeProvider.GetUtcNow() + retryIn;
                        retryDelay[i] = _NextRetryDelay(retryDelay[i], snapshot.Backstop);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    private async Task<bool> _TryLoadAsync(
        ISettingsSnapshotEntry snapshot,
        TimeSpan retryIn,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await snapshot.EnsureLoadedAsync(stoppingToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            LogInitialLoadFailed(logger, e, string.Join(", ", snapshot.Names), retryIn);

            return false;
        }
    }

    private async Task _ReloadAsync(ISettingsSnapshotEntry snapshot, CancellationToken stoppingToken)
    {
        try
        {
            await snapshot
                .ReloadAsync(SettingsSnapshotReloadReason.Backstop, announcedNames: null, stoppingToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // A failed re-read must not end the loop and with it the host; the next interval tries again.
            LogBackstopFailed(logger, e);
        }
    }

    private static TimeSpan _NextRetryDelay(TimeSpan current, TimeSpan backstop)
    {
        var doubled = current * 2;

        return doubled < backstop ? doubled : backstop;
    }

    private static TimeSpan _Jittered(TimeSpan interval)
    {
#pragma warning disable CA5394 // False positive: jitter only spreads replicas' reads and retries; nothing depends on it being unpredictable.
        return interval * (0.9 + (Random.Shared.NextDouble() * 0.2));
#pragma warning restore CA5394
    }

    [LoggerMessage(
        EventId = 4,
        EventName = "SettingsSnapshotBackstopFailed",
        Level = LogLevel.Error,
        Message = "A backstop re-read of a settings snapshot failed; retrying at the next interval"
    )]
    private static partial void LogBackstopFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5,
        EventName = "SettingsSnapshotInitialLoadFailed",
        Level = LogLevel.Warning,
        Message = "Loading the settings snapshot of {SettingNames} failed; retrying in {RetryDelay}"
    )]
    private static partial void LogInitialLoadFailed(
        ILogger logger,
        Exception exception,
        string settingNames,
        TimeSpan retryDelay
    );
}
