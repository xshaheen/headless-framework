// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Logging;

/// <summary>Limits which log entries a text pattern scans, so a pattern only pays its cost where the value can appear.</summary>
[PublicAPI]
public sealed class LogTextRedactionScope
{
    /// <summary>The lowest level scanned. Defaults to <see cref="LogLevel.Trace"/>, every level.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

    /// <summary>
    /// Category prefixes scanned, matched the way logging filters match them: ordinal, ignoring case. Empty, the
    /// default, scans every category.
    /// </summary>
    public IList<string> Categories { get; } = [];
}
