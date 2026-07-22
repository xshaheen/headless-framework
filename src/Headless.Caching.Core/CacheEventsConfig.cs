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
    /// Whether cache-event handlers run synchronously on the firing thread. Default <see langword="false"/>: handlers
    /// run on a background <see cref="System.Threading.Tasks.Task"/> so a slow or blocking handler cannot stall the
    /// cache operation. Enable only when deterministic ordering relative to the operation is required and handlers are
    /// known to be fast.
    /// </summary>
    public bool SyncHandlers { get; init; }

    /// <summary>The log level used to record an exception thrown by a synchronous cache-event handler. Default <see cref="LogLevel.Warning"/>.</summary>
    public LogLevel HandlerErrorLogLevel { get; init; } = LogLevel.Warning;
}
