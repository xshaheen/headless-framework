// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Resolves <see cref="ICache"/> instances registered under a name or role key.
/// </summary>
[PublicAPI]
public interface ICacheProvider
{
    /// <summary>Gets the cache registered under <paramref name="name"/>.</summary>
    /// <param name="name">The cache instance name or role key.</param>
    /// <returns>The resolved cache instance.</returns>
    /// <exception cref="InvalidOperationException">No cache is registered under <paramref name="name"/>.</exception>
    ICache GetCache(string name);

    /// <summary>Gets the cache registered under <paramref name="name"/>, or <see langword="null"/> when none is registered.</summary>
    /// <param name="name">The cache instance name or role key.</param>
    /// <returns>The resolved cache instance, or <see langword="null"/>.</returns>
    ICache? GetCacheOrNull(string name);

    /// <summary>
    /// Gets the names of all explicitly registered named cache instances, excluding the default unnamed cache and role keys.
    /// </summary>
    IReadOnlySet<string> RegisteredNames { get; }
}
