// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Couchbase;
using Couchbase.Transactions;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace Headless.Couchbase.Clusters;

using GetClusterResult = (ICluster Cluster, Transactions ClusterTransactions);

/// <summary>
/// Provides lazily-created, cached Couchbase cluster and transaction instances identified by a
/// logical cluster key. Disposing this provider disposes all created clusters.
/// </summary>
[PublicAPI]
public interface ICouchbaseClustersProvider : IAsyncDisposable
{
    /// <summary>
    /// Returns (or lazily creates) the cluster and transaction manager for <paramref name="clusterKey"/>.
    /// </summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="cancellationToken">
    /// A token to observe while waiting for the task to complete. Because clusters are created once and
    /// cached, the token governs the connection attempt that first materializes the cluster for a given
    /// <paramref name="clusterKey"/>; callers that receive an already-cached cluster complete synchronously
    /// and do not observe the token.
    /// </param>
    /// <returns>A tuple of the connected cluster and its transaction manager.</returns>
    ValueTask<GetClusterResult> GetClusterAsync(string clusterKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ICouchbaseClustersProvider"/> implementation that creates clusters on first
/// access and caches them for the lifetime of the provider.
/// </summary>
/// <remarks>
/// Cluster connections are initialized lazily and cached in a process-level static dictionary. This
/// means a single physical cluster is shared across all DI scopes within the process. Disposal
/// iterates all connected clusters and disposes them in sequence.
/// </remarks>
[PublicAPI]
public sealed class CouchbaseClustersProvider(
    ICouchbaseClusterOptionsProvider clusterOptionsProvider,
    ICouchbaseTransactionConfigProvider transactionConfigProvider,
    ILogger<CouchbaseClustersProvider> logger
) : ICouchbaseClustersProvider
{
    private static readonly ConcurrentDictionary<string, AsyncLazy<GetClusterResult>> _Clusters = new(
        StringComparer.Ordinal
    );

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="clusterKey"/> is null or empty.</exception>
    public async ValueTask<GetClusterResult> GetClusterAsync(
        string clusterKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotEmpty(clusterKey);

        // The token is captured by the first caller that populates the cache entry for this key; once the
        // cluster is cached the factory does not run again, so later callers reuse the connection regardless
        // of their own token. (The process-wide cache lifecycle is tracked separately and out of scope here.)
        return await _Clusters
            .GetOrAdd(
                clusterKey,
                static (clusterKey, state) => state.@this._CreateLazyClusterAsync(clusterKey, state.cancellationToken),
                (@this: this, cancellationToken)
            )
            .ConfigureAwait(false);
    }

    private AsyncLazy<GetClusterResult> _CreateLazyClusterAsync(string clusterKey, CancellationToken cancellationToken)
    {
        return new(async () =>
        {
            var clusterOptions = await clusterOptionsProvider
                .GetAsync(clusterKey, cancellationToken)
                .ConfigureAwait(false);
            var cluster = await Cluster.ConnectAsync(clusterOptions, cancellationToken).ConfigureAwait(false);

            var transactionConfig = await transactionConfigProvider
                .GetAsync(clusterKey, cancellationToken)
                .ConfigureAwait(false);
            var transactions = Transactions.Create(cluster, transactionConfig);

            try
            {
                await cluster.WaitUntilReadyAsync(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogFailedToConnectToCluster(e, clusterKey);
            }

            return (cluster, transactions);
        });
    }

    /// <summary>Disposes all connected clusters and their transaction managers.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var item in _Clusters)
        {
            if (item.Value is not { IsStarted: true, Task: { IsCompleted: true, IsFaulted: false, IsCanceled: false } })
            {
                continue;
            }

            var cluster = await item.Value.ConfigureAwait(false);

            await cluster.Cluster.DisposeAsync().ConfigureAwait(false);
            await cluster.ClusterTransactions.DisposeAsync().ConfigureAwait(false);
        }

        _Clusters.Clear();
    }
}

internal static partial class CouchbaseClustersProviderLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToConnectToCluster",
        Level = LogLevel.Error,
        Message = "Failed to connect to the cluster {ClusterKey}"
    )]
    public static partial void LogFailedToConnectToCluster(this ILogger logger, Exception exception, string clusterKey);
}
