// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>The kind of a refresh performed outside the caller's request path. Mirrors the <c>headless.cache.refresh_kind</c> metric dimension.</summary>
[PublicAPI]
public enum CacheRefreshKind
{
    /// <summary>An eager refresh started because a fresh hit passed the eager-refresh threshold.</summary>
    Eager,

    /// <summary>A background completion of a factory relegated after a soft timeout.</summary>
    Background,
}
