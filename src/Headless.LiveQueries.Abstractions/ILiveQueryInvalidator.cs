// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries;

/// <summary>Announces a committed change that no tracked data source reports: raw ADO, a webhook, another service.</summary>
/// <remarks>
/// Applications call this interface and the framework implements it. New members arrive as default interface
/// members, so an existing implementation keeps compiling.
/// </remarks>
[PublicAPI]
public interface ILiveQueryInvalidator
{
    /// <summary>
    /// Re-runs every live query in this process that depends on any of <paramref name="keys" />. Call it after the
    /// change is durable: a query that re-runs before the commit reads the old data and is not re-run again.
    /// </summary>
    /// <param name="keys">
    /// The dependency keys that changed, compared ordinally with the keys queries declared through
    /// <see cref="ILiveQueryDependencies.DependsOn" />.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the announcement before it is made.</param>
    /// <returns>A task that completes when every affected live query has been signalled.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="keys" /> holds a <see langword="null" /> or empty key.</exception>
    ValueTask InvalidateAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
}
