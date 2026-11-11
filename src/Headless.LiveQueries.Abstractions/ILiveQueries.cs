// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries;

/// <summary>Watches a query from .NET code: a hosted service, a Blazor component, a test.</summary>
/// <remarks>
/// Applications call this interface and the framework implements it. New members arrive as default interface
/// members, so an existing implementation keeps compiling.
/// </remarks>
[PublicAPI]
public interface ILiveQueries
{
    /// <summary>
    /// Runs <paramref name="query" /> now and again after every announced change to a dependency it recorded, and
    /// yields each result that differs from the last one yielded.
    /// </summary>
    /// <typeparam name="T">The result type. It must serialize to JSON, which is how two results are compared.</typeparam>
    /// <param name="query">
    /// The read-only query. Each run receives the provider of a new service scope and must record at least one
    /// dependency, through <see cref="ILiveQueryDependencies" /> or a tracked data source.
    /// </param>
    /// <param name="cancellationToken">Ends the subscription.</param>
    /// <returns>
    /// The results, starting with the first run's. Every run executes on the execution flow of the caller that
    /// enumerates the sequence, so ambient state such as the current tenant is the enumerating caller's.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="LiveQueryHasNoDependenciesException">
    /// Thrown while enumerating, when a run recorded no dependency.
    /// </exception>
    IAsyncEnumerable<LiveSnapshot<T>> WatchAsync<T>(
        Func<IServiceProvider, CancellationToken, ValueTask<T>> query,
        CancellationToken cancellationToken = default
    );
}
