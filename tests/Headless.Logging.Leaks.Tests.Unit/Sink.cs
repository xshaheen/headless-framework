// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Where an entry can end up.</summary>
public enum Sink
{
    /// <summary>
    /// The console a production host writes: Microsoft's JSON console formatter for a Microsoft.Extensions.Logging
    /// pipeline, Serilog's compact JSON formatter for a Serilog sink. The formatter's output is captured, not stdout.
    /// </summary>
    Console,

    /// <summary>A Serilog sink's text console, rendered from a message template.</summary>
    ConsoleText,

    /// <summary>A Serilog sink's rolling file.</summary>
    File,

    /// <summary>OTLP log export.</summary>
    OtlpLogs,

    /// <summary>OTLP trace export: span attributes, events, status.</summary>
    OtlpSpans,

    /// <summary>The context headers a propagator injects into an outgoing request or message.</summary>
    Propagation,
}
