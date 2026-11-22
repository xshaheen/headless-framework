// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Where an entry can end up.</summary>
public enum Sink
{
    /// <summary>
    /// The console a production host writes: Microsoft's JSON console formatter for a Microsoft.Extensions.Logging
    /// pipeline, Serilog's compact JSON formatter for a Serilog one. The formatter's output is captured, not stdout.
    /// </summary>
    Console,

    /// <summary>Serilog's text console in Development, rendered with the Headless output template.</summary>
    ConsoleText,

    /// <summary>The Headless Serilog profile's level files (<c>warning-.log</c> and siblings).</summary>
    File,

    /// <summary>OTLP log export.</summary>
    OtlpLogs,

    /// <summary>OTLP trace export: span attributes, events, status.</summary>
    OtlpSpans,

    /// <summary>The context headers a propagator injects into an outgoing request or message.</summary>
    Propagation,
}
