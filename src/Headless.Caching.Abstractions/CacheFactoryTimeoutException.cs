// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Exception thrown when a cache value factory exceeds its configured hard timeout without a fallback.</summary>
[PublicAPI]
public sealed class CacheFactoryTimeoutException : TimeoutException
{
    /// <summary>Initializes a new instance of the <see cref="CacheFactoryTimeoutException"/> class.</summary>
    /// <param name="key">The cache key whose factory timed out.</param>
    /// <param name="elapsed">How long the factory was allowed to run before timeout handling fired.</param>
    /// <param name="limit">The configured hard timeout limit.</param>
    public CacheFactoryTimeoutException(string key, TimeSpan elapsed, TimeSpan limit)
        : base(_BuildMessage(key, elapsed, limit))
    {
        Key = Argument.IsNotNullOrEmpty(key);
        Elapsed = elapsed;
        Limit = limit;
    }

    /// <summary>The cache key whose factory timed out.</summary>
    public string Key { get; }

    /// <summary>How long the factory was allowed to run before timeout handling fired.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>The configured hard timeout limit.</summary>
    public TimeSpan Limit { get; }

    private static string _BuildMessage(string key, TimeSpan elapsed, TimeSpan limit) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Cache factory timed out for key '{key}' after {elapsed:g}; limit was {limit:g}."
        );
}
