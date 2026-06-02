// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DistributedLocks.Redis;

/// <summary>
/// Keyed-DI service keys for the Redis distributed-lock package. The <see cref="ScriptsLoader"/> key
/// isolates this package's <c>HeadlessRedisScriptsLoader</c> (bound to the DI
/// <c>IConnectionMultiplexer</c>) from any other package that registers a loader against a different
/// multiplexer (for example <c>Headless.Caching.Redis</c>), so script preload, NOSCRIPT reset scope,
/// and EVALSHA evaluation all target the same Redis instance.
/// </summary>
internal static class RedisDistributedLockServiceKeys
{
    internal const string ScriptsLoader = "Headless.DistributedLocks.Redis:ScriptsLoader";
}
