// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Caching;

/// <summary>Represents an error that occurs when a cache value factory exceeds its configured hard timeout without a fallback value.</summary>
/// <param name="key">The cache key whose factory timed out.</param>
/// <param name="limit">The hard timeout limit that the factory exceeded.</param>
[PublicAPI]
public sealed class CacheFactoryTimeoutException(string key, TimeSpan limit)
    : TimeoutException(_BuildMessage(key, limit))
{
    /// <summary>Gets the cache key whose factory timed out.</summary>
    public string Key { get; } = Argument.IsNotNullOrEmpty(key);

    /// <summary>Gets the hard timeout limit that the factory exceeded.</summary>
    public TimeSpan Limit { get; } = limit;

    private static string _BuildMessage(string key, TimeSpan limit)
    {
        return string.Create(CultureInfo.InvariantCulture, $"Cache factory timed out for key '{key}' after {limit:g}.");
    }
}
