// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Caching-wide event-handler execution configuration resolved by every cache provider from DI. Registered once by
/// <c>AddHeadlessCaching</c> from the setup builder; providers thread it into their <see cref="CacheEventsHub"/>.
/// Must-be-public plumbing (DI resolves it into provider constructors); not intended for direct use.
/// </summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CacheEventsConfig
{
    /// <summary>
    /// Maximum signals buffered behind the active handler. Default 2,048. Producers never wait; a signal is dropped
    /// when this bounded FIFO is full.
    /// </summary>
    public int BufferCapacity { get; init; } = 2_048;

    /// <summary>
    /// How long cache disposal waits for accepted signals to drain before canceling the dispatcher. Default two
    /// seconds. Handlers should observe their cancellation token.
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The log level used to record an exception thrown by a cache-event handler. Default <see cref="LogLevel.Warning"/>.</summary>
    public LogLevel HandlerErrorLogLevel { get; init; } = LogLevel.Warning;
}
