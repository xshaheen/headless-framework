// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Caching;

/// <summary>Exception thrown when a cache value factory exceeds its configured hard timeout without a fallback.</summary>
/// <remarks>Initializes a new instance of the <see cref="CacheFactoryTimeoutException"/> class.</remarks>
/// <param name="key">The cache key whose factory timed out.</param>
/// <param name="limit">The configured hard timeout limit the factory exceeded.</param>
[PublicAPI]
public sealed class CacheFactoryTimeoutException(string key, TimeSpan limit)
    : TimeoutException(_BuildMessage(key, limit))
{
    /// <summary>The cache key whose factory timed out.</summary>
    public string Key { get; } = Argument.IsNotNullOrEmpty(key);

    /// <summary>The configured hard timeout limit the factory exceeded.</summary>
    public TimeSpan Limit { get; } = limit;

    private static string _BuildMessage(string key, TimeSpan limit) =>
        string.Create(CultureInfo.InvariantCulture, $"Cache factory timed out for key '{key}' after {limit:g}.");
}
