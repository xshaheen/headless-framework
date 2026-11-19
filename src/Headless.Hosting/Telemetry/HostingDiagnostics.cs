// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;

namespace Headless.Hosting;

/// <summary>
/// Names the <see cref="Meter" /> of the host's startup (<c>Headless.Hosting</c>): the duration of each startup
/// validator and each warm-up attempt, the time from process start until the host started, and the time from process
/// start until the warm-up readiness check first passed. Consumers subscribe via <see cref="MeterName" />
/// (<c>AddMeter</c>) or the typed <c>AddHostingInstrumentation()</c> extension on the OpenTelemetry meter provider
/// builder; nothing is recorded until something subscribes.
/// </summary>
[PublicAPI]
public static class HostingDiagnostics
{
    /// <summary>The full meter name used by the host's startup metrics (<c>Headless.Hosting</c>).</summary>
    public const string MeterName = HeadlessDiagnostics.Prefix + "Hosting";

    /// <summary>Shared <see cref="Meter" /> for the startup metrics (see <see cref="HostingTelemetry" />).</summary>
    internal static readonly Meter Meter = HeadlessDiagnostics.CreateMeter("Hosting");
}
