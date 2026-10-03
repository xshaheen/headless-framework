// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>The kind of a hybrid invalidation. Mirrors the <c>headless.cache.invalidation_kind</c> metric dimension.</summary>
[PublicAPI]
public enum CacheInvalidationKind
{
    /// <summary>A tag invalidation (<c>RemoveByTagAsync</c>).</summary>
    Tag,

    /// <summary>A logical whole-cache clear (<c>ClearAsync</c>).</summary>
    Clear,

    /// <summary>A whole-cache flush (<c>FlushAsync</c>).</summary>
    Flush,
}
