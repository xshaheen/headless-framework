// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Exception thrown when a cache value factory exceeds its configured hard timeout without a fallback.</summary>
[PublicAPI]
public sealed class CacheFactoryTimeoutException : TimeoutException
{
    /// <summary>Initializes a new instance of the <see cref="CacheFactoryTimeoutException"/> class.</summary>
    /// <param name="key">The cache key whose factory timed out.</param>
    /// <param name="limit">The configured hard timeout limit the factory exceeded.</param>
    public CacheFactoryTimeoutException(string key, TimeSpan limit)
        : base(_BuildMessage(key, limit))
    {
        Key = Argument.IsNotNullOrEmpty(key);
        Limit = limit;
    }

    /// <summary>The cache key whose factory timed out.</summary>
    public string Key { get; }

    /// <summary>The configured hard timeout limit the factory exceeded.</summary>
    public TimeSpan Limit { get; }

    private static string _BuildMessage(string key, TimeSpan limit) =>
        string.Create(CultureInfo.InvariantCulture, $"Cache factory timed out for key '{key}' after {limit:g}.");
}
