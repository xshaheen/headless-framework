// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Logging;

/// <summary>How the Headless console writes log entries.</summary>
[PublicAPI]
public enum HeadlessConsoleFormat
{
    /// <summary>One readable line per entry, with a UTC timestamp.</summary>
    Text,

    /// <summary>One JSON object per entry, with a UTC timestamp, the entry's properties, and its scopes.</summary>
    Json,
}
