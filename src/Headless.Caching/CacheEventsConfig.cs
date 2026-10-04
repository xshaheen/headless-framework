// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Headless.Caching;

/// <summary>
/// Provides configuration for caching event dispatcher execution.
/// </summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CacheEventsConfig
{
    /// <summary>
    /// Gets the maximum number of signals buffered behind the active handler. Defaults to 2,048.
    /// Producers do not wait; signals are dropped when the queue is full.
    /// </summary>
    public int BufferCapacity { get; init; } = 2_048;

    /// <summary>
    /// Gets how long cache disposal waits for accepted signals to drain before canceling the dispatcher.
    /// Defaults to two seconds.
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets the log level used when a cache event handler throws an exception. Defaults to <see cref="LogLevel.Warning"/>.</summary>
    public LogLevel HandlerErrorLogLevel { get; init; } = LogLevel.Warning;
}
