// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Resolves <see cref="ICache"/> instances registered under a name — either named instances added through the
/// name-taking provider setup overloads (for example <c>AddInMemoryCache("orders", …)</c>) or the role keys
/// documented on <c>CacheConstants</c> (<c>"memory"</c>, <c>"remote"</c>, <c>"hybrid"</c>).
/// </summary>
[PublicAPI]
public interface ICacheProvider
{
    /// <summary>Gets the cache registered under <paramref name="name"/>.</summary>
    /// <param name="name">The cache instance name (or role key).</param>
    /// <returns>The resolved cache.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no cache is registered under <paramref name="name"/>.</exception>
    ICache GetCache(string name);

    /// <summary>Gets the cache registered under <paramref name="name"/>, or <see langword="null"/> when none is registered.</summary>
    /// <param name="name">The cache instance name (or role key).</param>
    /// <returns>The resolved cache, or <see langword="null"/>.</returns>
    ICache? GetCacheOrNull(string name);
}
