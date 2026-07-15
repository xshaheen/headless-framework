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

    /// <summary>
    /// Per-instance loader key for named Redis caches: each named instance owns a loader bound to ITS
    /// multiplexer (named options may point at a different Redis than the default registration).
    /// </summary>
    internal static string NamedScriptsLoader(string name)
    {
        return $"{ScriptsLoader}:{name}";
    }

    /// <summary>Per-instance scripts initializer key for named Redis caches.</summary>
    internal static string NamedScriptsInitializer(string name)
    {
        return $"Headless.Caching.Redis:ScriptsInitializer:{name}";
    }
}
