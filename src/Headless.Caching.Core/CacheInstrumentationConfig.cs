// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;

namespace Headless.Caching;

/// <summary>
/// Provides configuration settings for caching telemetry and instrumentation.
/// </summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CacheInstrumentationConfig
{
    /// <summary>
    /// Gets a value indicating whether spans can include raw cache keys in the <c>headless.cache.key</c> attribute.
    /// Defaults to <see langword="false"/> to avoid emitting identifiers to trace backends.
    /// </summary>
    public bool IncludeKeyInTraces { get; init; }
}
