// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Headless.Sql;

/// <summary>
/// Names the <see cref="ActivitySource" /> and <see cref="Meter" /> of the SQL store kit (<c>Headless.Sql</c>). Every
/// autonomous store call that runs through <see cref="SqlAutonomousTransaction" />, and every Jobs claim that retries
/// through it, emits a span and metrics there. Consumers subscribe via <see cref="SourceName" />
/// (<c>AddSource</c>/<c>AddMeter</c>) or the typed <c>AddSqlInstrumentation()</c> extensions on the OpenTelemetry
/// provider builders; nothing is recorded until something subscribes.
/// </summary>
[PublicAPI]
public static class SqlDiagnostics
{
    /// <summary>The full activity-source and meter name used by the SQL store kit (<c>Headless.Sql</c>).</summary>
    public const string SourceName = HeadlessDiagnostics.Prefix + "Sql";

    /// <summary>Shared <see cref="ActivitySource" /> for the kit's traces.</summary>
    internal static readonly ActivitySource ActivitySource = HeadlessDiagnostics.CreateActivitySource("Sql");

    /// <summary>Shared <see cref="Meter" /> for the kit's metrics (see <see cref="SqlAutonomousTelemetry" />).</summary>
    internal static readonly Meter Meter = HeadlessDiagnostics.CreateMeter("Sql");
}
