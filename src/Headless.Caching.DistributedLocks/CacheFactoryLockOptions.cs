// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Options for the distributed factory-lock adapter bridging caching to <c>IDistributedLock</c>.</summary>
[PublicAPI]
public sealed class CacheFactoryLockOptions
{
    /// <summary>
    /// The prefix prepended to the cache key to form the distributed lock resource name. Defaults to
    /// <c>"cache:factory:"</c>; override it to namespace the cache locks away from other lock consumers
    /// sharing the same lock backend.
    /// </summary>
    public string ResourcePrefix { get; set; } = "cache:factory:";

    /// <summary>
    /// The lease TTL applied to each acquired factory lock. <see langword="null"/> (the default) uses the
    /// distributed lock provider's own default lease duration. The TTL is the backstop that frees the key
    /// when a node dies mid-factory, so keep it comfortably above the slowest expected factory run.
    /// </summary>
    public TimeSpan? TimeUntilExpires { get; set; }
}
