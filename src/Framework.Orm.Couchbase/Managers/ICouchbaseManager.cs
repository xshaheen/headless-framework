// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Collections.Concurrent;
using System.Text;
using Couchbase;
using Couchbase.Core.Exceptions;
using Couchbase.KeyValue;
using Couchbase.Management.Collections;
using Couchbase.Management.Query;
using Framework.Kernel.Checks;
using Framework.Orm.Couchbase.Clusters;
using Humanizer;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Framework.Orm.Couchbase.Managers;

public interface ICouchbaseManager
{
    Task<CreateScopeStatus> CreateScopeAsync(
        string clusterKey,
        string bucketName,
        string scopeName,
        CancellationToken cancellationToken = default
    );

    Task CreateCollectionsAsync(
        string clusterKey,
        string bucketName,
        string scopeName,
        HashSet<string> collections,
        CancellationToken cancellationToken = default
    );

    Task CreateSecondaryIndexAsync(
        string clusterKey,
        string bucketName,
        string scopeName,
        string collectionName,
        string indexName,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default
    );

    Task BuildDeferredIndexesAsync(string clusterKey, string bucketName, CancellationToken cancellationToken = default);
}

public enum CreateScopeStatus
{
    Exist,
    Failed,
    Success,
}

public sealed class CouchbaseManager : ICouchbaseManager
{
    private readonly ResiliencePipeline _retryPipeline;
    private readonly ICouchbaseClustersProvider _clustersProvider;
    private readonly ILogger<CouchbaseManager> _logger;

    public CouchbaseManager(ICouchbaseClustersProvider clustersProvider, ILogger<CouchbaseManager> logger)
    {
        _clustersProvider = clustersProvider;
        _logger = logger;

        var retryStrategyOptions = new RetryStrategyOptions
        {
            Name = "StormCouchbaseManager.Retry",
            BackoffType = DelayBackoffType.Exponential,
            Delay = 0.5.Seconds(),
            UseJitter = true,
            ShouldHandle = new PredicateBuilder().Handle<Exception>(e =>
                e
                    is not OperationCanceledException
                        and not TaskCanceledException
                        and not IndexExistsException
                        and not CollectionExistsException
            ),
        };

        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(retryStrategyOptions)
            .AddTimeout(10.Seconds())
            .Build();
    }

    public async Task BuildDeferredIndexesAsync(
        string clusterKey,
        string bucketName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(clusterKey);
        Argument.IsNotNull(bucketName);

        var (cluster, _) = await _clustersProvider.GetClusterAsync(clusterKey);

        _logger.LogInformation(
            "Cluster {ClusterKey} > Bucket {BucketName} > Try to build deferred indexes",
            clusterKey,
            bucketName
        );

        await cluster.QueryIndexes.BuildDeferredIndexesAsync(
            bucketName,
            options => options.CancellationToken(cancellationToken)
        );

        _logger.LogInformation(
            "Cluster {ClusterKey} > Bucket {BucketName} > Build deferred indexes success",
            clusterKey,
            bucketName
        );
    }

    public async Task<CreateScopeStatus> CreateScopeAsync(
        string clusterKey,
        string bucketName,
        string scopeName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(clusterKey);
        Argument.IsNotNull(bucketName);
        Argument.IsNotNull(scopeName);

        _logger.LogInformation(
            "Cluster {ClusterKey} > Bucket {BucketName} > Try to create scope {ScopeName}",
            clusterKey,
            bucketName,
            scopeName
        );

        var bucket = await _GetBucketAsync(clusterKey, bucketName);
        var bucketScopes = await _GetBucketScopeSpecsAsync(clusterKey, bucket);

        if (bucketScopes.ContainsKey(scopeName))
        {
            _logger.LogInformation(
                "Cluster {ClusterKey} > Bucket {BucketName} > Scope {ScopeName} exist",
                clusterKey,
                bucketName,
                scopeName
            );

            return CreateScopeStatus.Exist;
        }

        try
        {
            await bucket.Collections.CreateScopeAsync(scopeName);

            _ClearScopesCache(clusterKey, bucketName);

            _logger.LogInformation(
                "Cluster {ClusterKey} > Bucket {BucketName} > Create scope {ScopeName} success",
                clusterKey,
                bucketName,
                scopeName
            );

            return CreateScopeStatus.Success;
        }
        catch (ScopeExistsException)
        {
            _logger.LogInformation(
                "Cluster {ClusterKey} > Bucket {BucketName} > Create scope {ScopeName} success (exist)",
                clusterKey,
                bucketName,
                scopeName
            );

            return CreateScopeStatus.Success;
        }
        catch (Exception e)
        {
            _logger.LogError(
                e,
                "Cluster {ClusterKey} > Bucket {BucketName} > Create scope {ScopeName} failed",
                clusterKey,
                bucketName,
                scopeName
            );

            return CreateScopeStatus.Failed;
        }
    }

    public async Task CreateCollectionsAsync(
        string clusterKey,
        string bucketName,
        string scopeName,
        HashSet<string> collections,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(clusterKey);
        Argument.IsNotNull(bucketName);
        Argument.IsNotNull(scopeName);
        Argument.IsNotEmpty(collections);

        var bucket = await _GetBucketAsync(clusterKey, bucketName);
        var (scope, scopeCollections) = await _GetScopeAsync(clusterKey, bucket, scopeName);

        foreach (var collectionName in collections)
        {
            if (!scopeCollections.Contains(collectionName))
            {
                _logger.LogInformation(
                    "Cluster {ClusterKey} > Bucket {BucketName} > Try to create collection {ScopeName}.{CollectionName}",
                    clusterKey,
                    bucket.Name,
                    scope.Name,
                    collectionName
                );

                await _CreateCollectionAsync(bucket, scope, collectionName, cancellationToken);

                _logger.LogInformation(
                    "Cluster {ClusterKey} > Bucket {BucketName} > Create collection {ScopeName}.{CollectionName} success",
                    clusterKey,
                    bucket.Name,
                    scope.Name,
                    collectionName
                );

                await createPrimaryIndex(await scope.CollectionAsync(collectionName));
            }
            else
            {
                var collection = await scope.CollectionAsync(collectionName);

                if (!await _HasPrimaryIndexAsync(collection, cancellationToken))
                {
                    await createPrimaryIndex(collection);
                }
            }
        }

        return;

        async Task createPrimaryIndex(ICouchbaseCollection collection)
        {
            _logger.LogInformation(
                "Cluster {ClusterKey} > Bucket {BucketName} > Create collection {ScopeName}.{CollectionName} success",
                clusterKey,
                bucket.Name,
                scope.Name,
                collection.Name
            );

            await _CreatePrimaryIndexOnCollectionAsync(collection);

            _logger.LogInformation(
                "Cluster {ClusterKey} > Bucket {BucketName} > Create primary index on collection {ScopeName}.{CollectionName} success",
                clusterKey,
                bucket.Name,
                scope.Name,
                collection.Name
            );
        }
    }

    private async Task _CreateCollectionAsync(
        IBucket bucket,
        IScope scope,
        string collectionName,
        CancellationToken cancellationToken
    )
    {
        await _retryPipeline.ExecuteAsync(
            static async (state, token) =>
            {
                var (bucket, scope, collectionName) = state;

                await bucket.Collections.CreateCollectionAsync(
                    scope.Name,
                    collectionName,
                    CreateCollectionSettings.Default,
                    CreateCollectionOptions.Default.CancellationToken(token)
                );
            },
            (bucket, scope, collectionName),
            cancellationToken
        );
    }

    public async Task CreateSecondaryIndexAsync(
        string clusterKey,
        string bucketName,
        string scopeName,
        string collectionName,
        string indexName,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(clusterKey);
        Argument.IsNotNull(bucketName);
        Argument.IsNotNull(collectionName);
        Argument.IsNotNull(indexName);
        Argument.IsNotNullOrEmpty(fields);

        var bucket = await _GetBucketAsync(clusterKey, bucketName);
        var scope = await bucket.ScopeAsync(scopeName);
        var collection = await scope.CollectionAsync(collectionName);

        _logger.LogInformation(
            "Cluster {ClusterKey} > Bucket {BucketName} > Collection {ScopeName}.{CollectionName} > Try to create secondary index {IndexName}",
            clusterKey,
            bucketName,
            scopeName,
            collectionName,
            indexName
        );

        await _retryPipeline.ExecuteAsync(
            static async (state, token) =>
            {
                var (collection, indexName, fields) = state;

                var options = CreateQueryIndexOptions
                    .Default.IgnoreIfExists(true)
                    .Deferred(false)
                    .CancellationToken(token);

                await collection.QueryIndexes.CreateIndexAsync(indexName, fields, options);
            },
            (collection, indexName, fields),
            cancellationToken
        );

        _logger.LogInformation(
            "Cluster {ClusterKey} > Bucket {BucketName} > Collection {ScopeName}.{CollectionName} > Create secondary index {IndexName} success",
            clusterKey,
            bucketName,
            scopeName,
            collectionName,
            indexName
        );
    }

    #region Primary Index Helpers

    private static async Task<bool> _HasPrimaryIndexAsync(
        ICouchbaseCollection collection,
        CancellationToken cancellationToken
    )
    {
        var indexes = await collection.QueryIndexes.GetAllIndexesAsync(
            GetAllQueryIndexOptions.Default.CancellationToken(cancellationToken)
        );

        return indexes.Any(index => index.IsPrimary || string.Equals(index.Name, "#primary", StringComparison.Ordinal));
    }

    private async Task _CreatePrimaryIndexOnCollectionAsync(ICouchbaseCollection collection)
    {
        await _retryPipeline.ExecuteAsync(
            static async (collection, token) =>
            {
                var options = CreatePrimaryQueryIndexOptions
                    .Default.IndexName("#primary")
                    .IgnoreIfExists(ignoreIfExists: true)
                    .Deferred(deferred: false)
                    .CancellationToken(token);

                await collection.QueryIndexes.CreatePrimaryIndexAsync(options);
            },
            collection
        );
    }

    #endregion

    #region Helpers

    private async Task<ICluster> _GetClusterAsync(string clusterKey)
    {
        var (cluster, _) = await _clustersProvider.GetClusterAsync(clusterKey);

        return cluster;
    }

    private async Task<IBucket> _GetBucketAsync(string clusterKey, string bucketName)
    {
        var cluster = await _GetClusterAsync(clusterKey);
        var bucket = await cluster.BucketAsync(bucketName);

        return bucket;
    }

    private async Task<(IScope Scope, ISet<string> Collections)> _GetScopeAsync(
        string clusterKey,
        IBucket bucket,
        string scopeName
    )
    {
        var scopeSpecList = await _GetBucketScopeSpecsAsync(clusterKey, bucket);
        var scopeCollections = scopeSpecList[scopeName];

        // Note: `ScopeAsync` method is cached by the SDK
        var scope = await bucket.ScopeAsync(scopeName);

        return (scope, scopeCollections);
    }

    private readonly ConcurrentDictionary<string, Dictionary<string, HashSet<string>>> _scopesCache = [];
    private readonly CompositeFormat _scopesCacheKeyFormat = CompositeFormat.Parse("cluster:{0}:buckets:{1}");

    private string _GetScopesCacheKey(string clusterKey, string bucketName)
    {
        return string.Format(CultureInfo.InvariantCulture, _scopesCacheKeyFormat, clusterKey, bucketName);
    }

    private void _ClearScopesCache(string clusterKey, string bucketName)
    {
        var key = _GetScopesCacheKey(clusterKey, bucketName);
        _scopesCache.TryRemove(key, out _);
    }

    private async Task<Dictionary<string, HashSet<string>>> _GetBucketScopeSpecsAsync(string clusterKey, IBucket bucket)
    {
        var key = _GetScopesCacheKey(clusterKey, bucket.Name);

        if (_scopesCache.TryGetValue(key, out var scopes))
        {
            return scopes;
        }

        var scopesEnumerable = await bucket.Collections.GetAllScopesAsync();

        var mapped = scopesEnumerable.ToDictionary(
            x => x.Name,
            x => x.Collections.Select(y => y.Name).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal
        );

        _scopesCache.TryAdd(key, mapped);

        return mapped;
    }

    #endregion
}
