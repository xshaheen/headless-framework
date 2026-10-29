// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Logging;

/// <summary>The shape of the events the Headless Serilog console sink writes.</summary>
[PublicAPI]
public enum SerilogConsoleFormat
{
    /// <summary>One human-readable line per event, rendered with <see cref="SerilogFactory.OutputTemplate"/>.</summary>
    Text,

    /// <summary>
    /// One compact JSON object per line (CLEF, from <c>Serilog.Formatting.Compact</c>), carrying the message template,
    /// every event property, and the exception as fields a log collector indexes without parsing text.
    /// </summary>
    Json,
}
