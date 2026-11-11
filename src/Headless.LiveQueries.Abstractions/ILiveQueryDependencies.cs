// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries;

/// <summary>Lets a query declare a dependency that no tracked data source records for it.</summary>
/// <remarks>
/// Applications call this interface and the framework implements it. New members arrive as default interface
/// members, so an existing implementation keeps compiling.
/// </remarks>
[PublicAPI]
public interface ILiveQueryDependencies
{
    /// <summary>
    /// Makes the running live query depend on <paramref name="keys" />. Does nothing when the caller is not running
    /// as a live query, so a handler can call it unconditionally.
    /// </summary>
    /// <param name="keys">
    /// The dependency keys, compared ordinally with the keys passed to
    /// <see cref="ILiveQueryInvalidator.InvalidateAsync" />.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="keys" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="keys" /> holds a <see langword="null" /> or empty key.</exception>
    void DependsOn(params IEnumerable<string> keys);
}
