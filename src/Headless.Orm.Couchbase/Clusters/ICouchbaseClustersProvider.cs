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
public interface ICouchbaseClustersProvider : IAsyncDisposable
{
    /// <summary>
    /// Returns (or lazily creates) the cluster and transaction manager for <paramref name="clusterKey"/>.
    /// </summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <returns>A tuple of the connected cluster and its transaction manager.</returns>
    ValueTask<GetClusterResult> GetClusterAsync(string clusterKey);
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
    public async ValueTask<GetClusterResult> GetClusterAsync(string clusterKey)
    {
        Argument.IsNotEmpty(clusterKey);

        return await _Clusters.GetOrAdd(
            clusterKey,
            static (clusterKey, @this) => @this._CreateLazyClusterAsync(clusterKey),
            this
        );
    }

    private AsyncLazy<GetClusterResult> _CreateLazyClusterAsync(string clusterKey)
    {
        return new(async () =>
        {
            var clusterOptions = await clusterOptionsProvider.GetAsync(clusterKey);
            var cluster = await Cluster.ConnectAsync(clusterOptions);

            var transactionConfig = await transactionConfigProvider.GetAsync(clusterKey);
            var transactions = Transactions.Create(cluster, transactionConfig);

            try
            {
                await cluster.WaitUntilReadyAsync(TimeSpan.FromMinutes(1));
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

            var cluster = await item.Value;

            await cluster.Cluster.DisposeAsync();
            await cluster.ClusterTransactions.DisposeAsync();
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
