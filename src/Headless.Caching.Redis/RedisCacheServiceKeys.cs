// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Keyed-DI service keys for the Redis cache package. The <see cref="ScriptsLoader"/> key isolates
/// this package's <c>HeadlessRedisScriptsLoader</c> (bound to <c>RedisCacheOptions.ConnectionMultiplexer</c>)
/// from any other package that registers a loader against a different multiplexer (for example
/// <c>Headless.DistributedLocks.Redis</c>), so script preload, NOSCRIPT reset scope, and EVALSHA
/// evaluation all target the same Redis instance.
/// </summary>
internal static class RedisCacheServiceKeys
{
    internal const string ScriptsLoader = "Headless.Caching.Redis:ScriptsLoader";
}
