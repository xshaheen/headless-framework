// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase;
using Headless.Couchbase.Clusters;
using Headless.Couchbase.Context;

namespace Headless.Couchbase.ContextProvider;

/// <summary>
/// Creates and initializes typed <see cref="CouchbaseBucketContext"/> subclasses by resolving the
/// cluster, opening the bucket, and wiring all <c>IDocumentSet&lt;T&gt;</c> properties.
/// </summary>
[PublicAPI]
public interface IBucketContextProvider
{
    /// <summary>
    /// Returns an initialized <typeparamref name="T"/> context connected to the specified cluster and
    /// bucket, with all document-set properties wired to their Couchbase scopes and collections.
    /// </summary>
    /// <typeparam name="T">The <see cref="CouchbaseBucketContext"/> subclass to create.</typeparam>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="bucketName">The Couchbase bucket to open.</param>
    /// <param name="defaultScopeName">
    /// When provided, all document sets are placed in this scope with their original scope name
    /// prepended to the collection name (multi-tenant per scope). Pass <see langword="null"/> to use
    /// each property's declared scope directly.
    /// </param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>An initialized <typeparamref name="T"/> context.</returns>
    ValueTask<T> GetAsync<T>(
        string clusterKey,
        string bucketName,
        string? defaultScopeName,
        CancellationToken cancellationToken = default
    )
        where T : CouchbaseBucketContext;
}

/// <summary>Default <see cref="IBucketContextProvider"/> implementation.</summary>
[PublicAPI]
public sealed class BucketContextProvider(
    ICouchbaseClustersProvider couchbaseClustersProvider,
    IServiceProvider serviceProvider
) : IBucketContextProvider
{
    /// <inheritdoc/>
    public async ValueTask<T> GetAsync<T>(
        string clusterKey,
        string bucketName,
        string? defaultScopeName,
        CancellationToken cancellationToken = default
    )
        where T : CouchbaseBucketContext
    {
        var connection = await couchbaseClustersProvider
            .GetClusterAsync(clusterKey, cancellationToken)
            .ConfigureAwait(false);

        // Couchbase's ICluster.BucketAsync exposes no CancellationToken overload, so honor the token before
        // opening the bucket; it is not observed for the duration of the (typically cached) bucket open.
        cancellationToken.ThrowIfCancellationRequested();
        var bucket = await _GetBucketAsync(connection.Cluster, bucketName).ConfigureAwait(false);

        return CouchbaseBucketContextInitializer.Initialize<T>(
            serviceProvider,
            bucket,
            connection.Transactions,
            defaultScopeName
        );
    }

    private static ValueTask<IBucket> _GetBucketAsync(ICluster cluster, string bucketName)
    {
        // Maybe cache this if not cached by the cluster
        return cluster.BucketAsync(bucketName);
    }
}
