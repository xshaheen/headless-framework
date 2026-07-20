// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Couchbase;
using Couchbase.Transactions;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace Headless.Couchbase.Clusters;

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
    /// A token that bounds only <em>this</em> caller's wait for the cluster. The underlying connection is a
    /// provider-shared, must-complete operation that always runs on <see cref="CancellationToken.None"/>, so
    /// cancelling this token abandons the caller's own wait without aborting — or permanently poisoning — the
    /// shared connection that other callers are awaiting. A connection attempt that fails is evicted so the
    /// next caller retries; callers that receive an already-connected cluster complete synchronously.
    /// </param>
    /// <returns>The connected cluster and its transaction manager.</returns>
    ValueTask<CouchbaseClusterConnection> GetClusterAsync(
        string clusterKey,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Default <see cref="ICouchbaseClustersProvider"/> implementation that creates clusters on first
/// access and caches them for the lifetime of the provider.
/// </summary>
/// <remarks>
/// Cluster connections are initialized lazily and cached per provider instance.
/// <c>AddHeadlessCouchbase</c> registers the provider as a singleton, so within one container a single
/// physical cluster is shared across all DI scopes; separate containers (or separately constructed
/// providers with different option providers) hold independent connections instead of silently sharing
/// a process-global cache. The shared connect runs to completion on <see cref="CancellationToken.None"/>
/// so that no single caller's cancellation can abort or poison the connection others depend on; a connect
/// that faults or is cancelled is evicted from the cache so the next caller re-creates it. Disposal
/// iterates all connected clusters and disposes them in sequence.
/// </remarks>
[PublicAPI]
public sealed class CouchbaseClustersProvider(
    ICouchbaseClusterOptionsProvider clusterOptionsProvider,
    ICouchbaseTransactionConfigProvider transactionConfigProvider,
    ILogger<CouchbaseClustersProvider> logger
) : ICouchbaseClustersProvider
{
    private readonly ConcurrentDictionary<string, AsyncLazy<CouchbaseClusterConnection>> _clusters = new(
        StringComparer.Ordinal
    );

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="clusterKey"/> is null or empty.</exception>
    public async ValueTask<CouchbaseClusterConnection> GetClusterAsync(
        string clusterKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotEmpty(clusterKey);

        var lazy = _clusters.GetOrAdd(
            clusterKey,
            static (clusterKey, @this) => @this._CreateLazyCluster(clusterKey),
            this
        );

        try
        {
            // The shared connect (see _CreateLazyCluster) is must-complete and runs on CancellationToken.None;
            // the caller's token bounds only *this* wait via WaitAsync, so a caller can abandon their own wait
            // without cancelling — or, under AsyncLazyFlags.None, permanently poisoning — the shared task that
            // other callers are still awaiting.
            return await lazy.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // AsyncLazy caches failures (AsyncLazyFlags.None), so a faulted/cancelled shared connect would be
            // replayed for every future caller. Evict it — only when the *shared* task itself ended badly, not
            // when this caller merely cancelled their own wait — so the next caller re-creates it. TryRemove of
            // the exact key/value pair removes only if this AsyncLazy is still cached, avoiding a race with a
            // healthy replacement another caller may have already installed.
            if (lazy.Task.IsFaulted || lazy.Task.IsCanceled)
            {
                _clusters.TryRemove(new KeyValuePair<string, AsyncLazy<CouchbaseClusterConnection>>(clusterKey, lazy));
            }

            throw;
        }
    }

    private AsyncLazy<CouchbaseClusterConnection> _CreateLazyCluster(string clusterKey)
    {
        // The connection is a provider-shared resource cached and reused by every caller, so the factory runs
        // on CancellationToken.None: one caller cancelling their GetClusterAsync wait must never abort — or,
        // because AsyncLazy caches failures, permanently poison — the connection the others depend on.
        return new(async () =>
        {
            var clusterOptions = await clusterOptionsProvider
                .GetAsync(clusterKey, CancellationToken.None)
                .ConfigureAwait(false);
            var cluster = await Cluster.ConnectAsync(clusterOptions, CancellationToken.None).ConfigureAwait(false);

            var transactionConfig = await transactionConfigProvider
                .GetAsync(clusterKey, CancellationToken.None)
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

            return new CouchbaseClusterConnection { Cluster = cluster, Transactions = transactions };
        });
    }

    /// <summary>Disposes all connected clusters and their transaction managers.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var item in _clusters)
        {
            if (item.Value is not { IsStarted: true, Task: { IsCompleted: true, IsFaulted: false, IsCanceled: false } })
            {
                continue;
            }

            var connection = await item.Value.ConfigureAwait(false);

            await connection.Cluster.DisposeAsync().ConfigureAwait(false);
            await connection.Transactions.DisposeAsync().ConfigureAwait(false);
        }

        _clusters.Clear();
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
