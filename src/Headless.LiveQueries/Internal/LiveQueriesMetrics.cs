// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;

namespace Headless.LiveQueries.Internal;

/// <summary>The instruments of the <see cref="LiveQueriesDiagnostics.SourceName" /> meter. None carries a tag.</summary>
internal sealed class LiveQueriesMetrics
{
    internal const string ActiveSubscriptionsName = "headless.live_queries.subscriptions.active";
    internal const string RunsName = "headless.live_queries.runs";
    internal const string FailedRunsName = "headless.live_queries.runs.failed";
    internal const string WakesName = "headless.live_queries.wakes";
    internal const string RowsRuledOutName = "headless.live_queries.rows.ruled_out";

    private readonly UpDownCounter<long> _activeSubscriptions;
    private readonly Counter<long> _runs;
    private readonly Counter<long> _failedRuns;
    private readonly Counter<long> _wakes;
    private readonly Counter<long> _rowsRuledOut;

    /// <summary>Creates the instruments.</summary>
    /// <param name="meterFactory">
    /// The host's meter factory. A host that never called <c>AddMetrics</c> has none, so the instruments are then
    /// created on the process's shared meter and stay visible to a process-wide listener.
    /// </param>
    public LiveQueriesMetrics(IMeterFactory? meterFactory = null)
    {
#pragma warning disable CA2000 // False positive: the factory disposes the meters it creates, and the shared meter lives as long as the process.
        var meter = LiveQueriesDiagnostics.GetMeter(meterFactory);
#pragma warning restore CA2000

        _activeSubscriptions = meter.CreateUpDownCounter<long>(
            ActiveSubscriptionsName,
            description: "Live-query subscriptions currently open in this process"
        );
        _runs = meter.CreateCounter<long>(RunsName, description: "Runs of a live query, first runs included");
        _failedRuns = meter.CreateCounter<long>(
            FailedRunsName,
            description: "Runs of a live query that threw or reported a failure"
        );
        _wakes = meter.CreateCounter<long>(
            WakesName,
            description: "Times an announced change signalled a subscription to run again"
        );
        _rowsRuledOut = meter.CreateCounter<long>(
            RowsRuledOutName,
            description: "Changed rows that a subscription's row filters showed could not affect its result"
        );
    }

    public void SubscriptionOpened() => _activeSubscriptions.Add(1);

    public void SubscriptionClosed() => _activeSubscriptions.Add(-1);

    public void RunStarted() => _runs.Add(1);

    public void RunFailed() => _failedRuns.Add(1);

    public void SubscriptionWoken() => _wakes.Add(1);

    public void RowsRuledOut(int rows) => _rowsRuledOut.Add(rows);
}
