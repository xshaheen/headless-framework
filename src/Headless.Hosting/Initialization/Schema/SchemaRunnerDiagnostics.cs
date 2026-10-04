// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Headless.Hosting.Initialization.Schema;

/// <summary>
/// Names the <see cref="ActivitySource" /> and <see cref="Meter" /> of the schema runner
/// (<c>Headless.SchemaRunner</c>): one span per apply or verify pass with child spans for the lock wait and each
/// applied step, and metrics for pass duration, lock wait, applied and skipped steps, history mismatches, and absorbed
/// races. Consumers subscribe via <see cref="SourceName" /> (<c>AddSource</c>/<c>AddMeter</c>) or the typed
/// <c>AddSchemaRunnerInstrumentation()</c> extensions on the OpenTelemetry provider builders; nothing is recorded
/// until something subscribes.
/// </summary>
[PublicAPI]
public static class SchemaRunnerDiagnostics
{
    /// <summary>The full activity-source and meter name used by the schema runner (<c>Headless.SchemaRunner</c>).</summary>
    public const string SourceName = HeadlessDiagnostics.Prefix + "SchemaRunner";

    /// <summary>Shared <see cref="ActivitySource" /> for the runner's traces.</summary>
    internal static readonly ActivitySource ActivitySource = HeadlessDiagnostics.CreateActivitySource("SchemaRunner");

    /// <summary>Shared <see cref="Meter" /> for the runner's metrics (see <see cref="SchemaRunnerTelemetry" />).</summary>
    internal static readonly Meter Meter = HeadlessDiagnostics.CreateMeter("SchemaRunner");
}
