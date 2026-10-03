// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>The direction of a hybrid invalidation relative to this instance. Mirrors the <c>headless.cache.direction</c> metric dimension.</summary>
[PublicAPI]
public enum CacheInvalidationDirection
{
    /// <summary>This instance published the invalidation to peers.</summary>
    Publish,

    /// <summary>This instance received the invalidation from a peer.</summary>
    Receive,
}
