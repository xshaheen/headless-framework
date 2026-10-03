// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>The outcome of a factory execution or a refresh. Mirrors the <c>headless.cache.outcome</c> metric dimension.</summary>
[PublicAPI]
public enum CacheFactoryOutcome
{
    /// <summary>The factory completed successfully.</summary>
    Success,

    /// <summary>The factory threw.</summary>
    Error,

    /// <summary>The factory timed out.</summary>
    Timeout,
}
