// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Caching;

/// <summary>Exception thrown when a cache value factory exceeds its configured hard timeout without a fallback.</summary>
/// <param name="key">The cache key whose factory timed out.</param>
/// <param name="limit">The configured hard timeout limit the factory exceeded.</param>
/// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
/// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
[PublicAPI]
public sealed class CacheFactoryTimeoutException(string key, TimeSpan limit)
    : TimeoutException(_BuildMessage(key, limit))
{
    /// <summary>Gets the cache key whose factory timed out.</summary>
    public string Key { get; } = Argument.IsNotNullOrEmpty(key);

    /// <summary>Gets the configured hard timeout limit the factory exceeded.</summary>
    public TimeSpan Limit { get; } = limit;

    private static string _BuildMessage(string key, TimeSpan limit)
    {
        return string.Create(CultureInfo.InvariantCulture, $"Cache factory timed out for key '{key}' after {limit:g}.");
    }
}
