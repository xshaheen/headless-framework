// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase;
using Headless.Couchbase.Clusters;
using Headless.Couchbase.Context;

namespace Headless.Couchbase.ContextProvider;

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
