// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Settings.Values;

/// <summary>Loads every registered settings snapshot when the host starts, then re-reads each on its backstop interval.</summary>
/// <remarks>
/// The first load runs in <see cref="StartAsync"/>, after every hosted service's <c>StartingAsync</c>, so the relational
/// settings schema exists by then whatever order the host registered settings and snapshots in. A failure there fails
/// the host: a process must not serve on a policy it never read.
/// </remarks>
internal sealed partial class SettingsSnapshotHostedService(
    IEnumerable<ISettingsSnapshotEntry> snapshots,
    TimeProvider timeProvider,
    ILogger<SettingsSnapshotHostedService> logger
) : BackgroundService
{
    private readonly ISettingsSnapshotEntry[] _snapshots = [.. snapshots];

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var snapshot in _snapshots)
        {
            await snapshot.LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_snapshots.Length == 0)
        {
            return;
        }

        // Each snapshot keeps its own jittered due time, so different intervals stay independent and replicas started
        // together do not read in lockstep.
        var due = new DateTimeOffset[_snapshots.Length];
        var now = timeProvider.GetUtcNow();

        for (var i = 0; i < _snapshots.Length; i++)
        {
            due[i] = now + _Jittered(_snapshots[i].Backstop);
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

                    try
                    {
                        await _snapshots[i]
                            .ReloadAsync(SettingsSnapshotReloadReason.Backstop, announcedNames: null, stoppingToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception e)
                        when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                    {
                        // A failed re-read must not end the loop and with it the host; the next interval tries again.
                        LogBackstopFailed(logger, e);
                    }

                    due[i] = timeProvider.GetUtcNow() + _Jittered(_snapshots[i].Backstop);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    [LoggerMessage(
        EventId = 4,
        EventName = "SettingsSnapshotBackstopFailed",
        Level = LogLevel.Error,
        Message = "A backstop re-read of a settings snapshot failed; retrying at the next interval"
    )]
    private static partial void LogBackstopFailed(ILogger logger, Exception exception);

    private static TimeSpan _Jittered(TimeSpan interval)
    {
#pragma warning disable CA5394 // False positive: jitter only spreads replicas' re-reads; nothing depends on it being unpredictable.
        return interval * (0.9 + (Random.Shared.NextDouble() * 0.2));
#pragma warning restore CA5394
    }
}
