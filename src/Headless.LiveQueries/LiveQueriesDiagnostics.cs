// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;

namespace Headless.LiveQueries;

/// <summary>
/// Names the <see cref="Meter" /> of the live-queries subsystem. The hub, live endpoints, and tracked data sources
/// record on one meter, so their metrics land in a single named scope (<c>Headless.LiveQueries</c>). Consumers
/// subscribe with <c>AddMeter(</c><see cref="SourceName" /><c>)</c> or the typed
/// <c>AddLiveQueriesInstrumentation()</c> extension on the OpenTelemetry meter provider builder.
/// </summary>
[PublicAPI]
public static class LiveQueriesDiagnostics
{
    /// <summary>The meter name used by the live-queries subsystem (<c>Headless.LiveQueries</c>).</summary>
    public const string SourceName = HeadlessDiagnostics.Prefix + "LiveQueries";

    /// <summary>
    /// The meter of a host that registered no <see cref="IMeterFactory" />. It is shared by the process and visible
    /// to a process-wide listener.
    /// </summary>
    internal static readonly Meter Meter = HeadlessDiagnostics.CreateMeter("LiveQueries");

    /// <summary>
    /// Returns the meter the subsystem records on: one from <paramref name="meterFactory" />, which owns and
    /// disposes it with the host, else the shared one. Both carry the framework's version.
    /// </summary>
    internal static Meter GetMeter(IMeterFactory? meterFactory)
    {
        return meterFactory?.Create(SourceName, Meter.Version) ?? Meter;
    }
}
