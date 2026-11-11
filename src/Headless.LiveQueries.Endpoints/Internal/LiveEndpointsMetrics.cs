// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// The instruments live endpoints add to the <see cref="LiveQueriesDiagnostics.SourceName" /> meter. Open streams are not counted
/// here: each is one of the meter's active subscriptions. None carries a tag.
/// </summary>
internal sealed class LiveEndpointsMetrics
{
    internal const string RejectedStreamsName = "headless.live_queries.streams.rejected";
    internal const string QueuedRerunsName = "headless.live_queries.reruns.queued";

    private readonly Counter<long> _rejectedStreams;
    private readonly Counter<long> _queuedReruns;

    /// <summary>Creates the instruments.</summary>
    /// <param name="meterFactory">
    /// The host's meter factory. A host that never called <c>AddMetrics</c> has none, so the instruments are then
    /// created on the process's shared meter and stay visible to a process-wide listener.
    /// </param>
    public LiveEndpointsMetrics(IMeterFactory? meterFactory = null)
    {
#pragma warning disable CA2000 // False positive: the factory disposes the meters it creates, and the shared meter lives as long as the process.
        var meter = LiveQueriesDiagnostics.GetMeter(meterFactory);
#pragma warning restore CA2000

        _rejectedStreams = meter.CreateCounter<long>(
            RejectedStreamsName,
            description: "Streaming requests refused because the caller or the host held the allowed number of streams"
        );
        _queuedReruns = meter.CreateCounter<long>(
            QueuedRerunsName,
            description: "Re-runs that waited because the allowed number of re-runs was already executing"
        );
    }

    public void StreamRejected() => _rejectedStreams.Add(1);

    public void RerunQueued() => _queuedReruns.Add(1);
}
