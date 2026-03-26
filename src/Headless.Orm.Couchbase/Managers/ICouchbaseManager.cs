// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Couchbase;
using Couchbase.Core.Exceptions;
using Couchbase.KeyValue;
using Couchbase.Management.Collections;
using Couchbase.Management.Query;
using Headless.Checks;
using Headless.Orm.Couchbase.Clusters;
using Humanizer;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Headless.Orm.Couchbase.Managers;

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
            Name = "CouchbaseManager.Retry",
            BackoffType = DelayBackoffType.Linear,
            Delay = 0.5.Seconds(),
            UseJitter = true,
            ShouldHandle = new PredicateBuilder().Handle<Exception>(e =>
                e
                    is not OperationCanceledException
                        and not TaskCanceledException
                        and not IndexExistsException
                        and not CollectionExistsException
                        and not ScopeExistsException
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

        var timestamp = Stopwatch.GetTimestamp();
        var (cluster, _) = await _clustersProvider.GetClusterAsync(clusterKey);

        await _retryPipeline.ExecuteAsync(
            static async (state, token) =>
            {
                await state.cluster.QueryIndexes.BuildDeferredIndexesAsync(
                    state.bucketName,
                    options => options.CancellationToken(token)
                );
            },
            (cluster, bucketName),
            cancellationToken
        );

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.BuildDeferredIndexesSucceeded(clusterKey, bucketName, Stopwatch.GetElapsedTime(timestamp));
        }
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

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.TryCreateScope(clusterKey, bucketName, scopeName);
        }

        var bucket = await _GetBucketAsync(clusterKey, bucketName);
        var bucketScopes = await _GetBucketScopeSpecsAsync(clusterKey, bucket);

        if (bucketScopes.ContainsKey(scopeName))
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.ScopeExists(clusterKey, bucketName, scopeName);
            }

            return CreateScopeStatus.Exist;
        }

        try
        {
            return await _retryPipeline.ExecuteAsync(
                static async (state, token) =>
                {
                    var (@this, clusterKey, scopeName, bucket) = state;

                    try
                    {
                        await bucket.Collections.CreateScopeAsync(
                            scopeName,
                            options => options.CancellationToken(token)
                        );

                        @this._ClearScopesCache(clusterKey, bucket.Name);

                        if (@this._logger.IsEnabled(LogLevel.Information))
                        {
                            @this._logger.CreateScopeSucceeded(clusterKey, bucket.Name, scopeName);
                        }

                        return CreateScopeStatus.Success;
                    }
                    catch (ScopeExistsException)
                    {
                        if (@this._logger.IsEnabled(LogLevel.Information))
                        {
                            @this._logger.CreateScopeSucceededAlreadyExists(clusterKey, bucket.Name, scopeName);
                        }

                        return CreateScopeStatus.Success;
                    }
                },
                (this, clusterKey, scopeName, bucket),
                cancellationToken
            );
        }
        catch (Exception e)
        {
            _logger.CreateScopeFailed(e, clusterKey, bucketName, scopeName);

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

        var timestamp = Stopwatch.GetTimestamp();
        var bucket = await _GetBucketAsync(clusterKey, bucketName);
        var (scope, scopeCollections) = await _GetScopeAsync(clusterKey, bucket, scopeName);

        await Parallel.ForEachAsync(
            collections,
            cancellationToken,
            async (collectionName, token) =>
            {
                if (scopeCollections.Contains(collectionName))
                {
                    var collection = await scope.CollectionAsync(collectionName);

                    if (!await _HasPrimaryIndexAsync(collection, token))
                    {
                        await _CreatePrimaryIndexOnCollectionAsync(clusterKey, collection);
                    }

                    return;
                }

                await _CreateCollectionAsync(clusterKey, bucket, scope, collectionName, token);
                await Task.Delay(50.Milliseconds(), token);
                await _CreatePrimaryIndexOnCollectionAsync(clusterKey, await scope.CollectionAsync(collectionName));
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(timestamp);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.CreateAllCollectionsSucceeded(clusterKey, bucketName, scopeName, elapsed);
        }
    }

    private async Task _CreateCollectionAsync(
        string clusterKey,
        IBucket bucket,
        IScope scope,
        string collectionName,
        CancellationToken token
    )
    {
        var timestamp = Stopwatch.GetTimestamp();

        try
        {
            await _retryPipeline.ExecuteAsync(
                static async (state, token) =>
                {
                    var (bucket, scope, collectionName) = state;

                    try
                    {
                        await bucket.Collections.CreateCollectionAsync(
                            scope.Name,
                            collectionName,
                            CreateCollectionSettings.Default,
                            CreateCollectionOptions.Default.CancellationToken(token)
                        );
                    }
                    catch (CollectionExistsException)
                    {
                        // Ignore if a collection already exists it's same as success
                    }
                },
                (bucket, scope, collectionName),
                token
            );

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.CreateCollectionSucceeded(
                    clusterKey,
                    bucket.Name,
                    scope.Name,
                    collectionName,
                    Stopwatch.GetElapsedTime(timestamp)
                );
            }
        }
        catch (Exception e)
        {
            _logger.CreateCollectionFailed(
                e,
                clusterKey,
                bucket.Name,
                scope.Name,
                collectionName,
                Stopwatch.GetElapsedTime(timestamp)
            );

            throw;
        }
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

        var timestamp = Stopwatch.GetTimestamp();
        var bucket = await _GetBucketAsync(clusterKey, bucketName);
        var scope = await bucket.ScopeAsync(scopeName);
        var collection = await scope.CollectionAsync(collectionName);

        try
        {
            await _retryPipeline.ExecuteAsync(
                static async (state, token) =>
                {
                    var (collection, indexName, fields) = state;

                    var options = CreateQueryIndexOptions
                        .Default.IgnoreIfExists(ignoreIfExists: true)
                        .Deferred(deferred: false)
                        .Timeout(5.Seconds())
                        .CancellationToken(token);

                    await collection.QueryIndexes.CreateIndexAsync(indexName, fields, options);
                },
                (collection, indexName, fields),
                cancellationToken
            );

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.CreateSecondaryIndexSucceeded(
                    clusterKey,
                    bucketName,
                    scopeName,
                    collectionName,
                    indexName,
                    Stopwatch.GetElapsedTime(timestamp)
                );
            }
        }
        catch (Exception e)
        {
            _logger.CreateSecondaryIndexFailed(
                e,
                clusterKey,
                collection.Scope.Bucket.Name,
                collection.Scope.Name,
                collection.Name,
                indexName,
                Stopwatch.GetElapsedTime(timestamp)
            );

            throw;
        }
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

        return indexes.Any(index =>
            index.IsPrimary || string.Equals(index.Name, "#primary", StringComparison.OrdinalIgnoreCase)
        );
    }

    private async Task _CreatePrimaryIndexOnCollectionAsync(string clusterKey, ICouchbaseCollection collection)
    {
        var timestamp = Stopwatch.GetTimestamp();

        try
        {
            await _retryPipeline.ExecuteAsync(
                static async (collection, token) =>
                {
                    var options = CreatePrimaryQueryIndexOptions
                        .Default.IndexName("#primary")
                        .IgnoreIfExists(ignoreIfExists: true)
                        .Timeout(5.Seconds())
                        .Deferred(deferred: false)
                        .CancellationToken(token);

                    await collection.QueryIndexes.CreatePrimaryIndexAsync(options);
                },
                collection
            );

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.CreatePrimaryIndexSucceeded(
                    clusterKey,
                    collection.Scope.Bucket.Name,
                    collection.Scope.Name,
                    collection.Name,
                    Stopwatch.GetElapsedTime(timestamp)
                );
            }
        }
        catch (Exception e)
        {
            _logger.CreatePrimaryIndexFailed(
                e,
                clusterKey,
                collection.Scope.Bucket.Name,
                collection.Scope.Name,
                collection.Name,
                Stopwatch.GetElapsedTime(timestamp)
            );

            throw;
        }
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

internal static partial class CouchbaseManagerLog
{
    [LoggerMessage(
        EventId = 5100,
        Level = LogLevel.Information,
        Message = "Cluster {ClusterKey} > Bucket {BucketName} > Build deferred indexes SUCCESS took {Elapsed}"
    )]
    public static partial void BuildDeferredIndexesSucceeded(
        this ILogger logger,
        string clusterKey,
        string bucketName,
        TimeSpan elapsed
    );

    [LoggerMessage(
        EventId = 5101,
        Level = LogLevel.Information,
        Message = "Cluster {ClusterKey} > Bucket {BucketName} > Try to create scope {ScopeName}"
    )]
    public static partial void TryCreateScope(
        this ILogger logger,
        string clusterKey,
        string bucketName,
        string scopeName
    );

    [LoggerMessage(
        EventId = 5102,
        Level = LogLevel.Information,
        Message = "Cluster {ClusterKey} > Bucket {BucketName} > Scope {ScopeName} exist"
    )]
    public static partial void ScopeExists(this ILogger logger, string clusterKey, string bucketName, string scopeName);

    [LoggerMessage(
        EventId = 5103,
        Level = LogLevel.Information,
        Message = "Cluster {ClusterKey} > Bucket {BucketName} > Create scope {ScopeName} success"
    )]
    public static partial void CreateScopeSucceeded(
        this ILogger logger,
        string clusterKey,
        string bucketName,
        string scopeName
    );

    [LoggerMessage(
        EventId = 5104,
        Level = LogLevel.Information,
        Message = "Cluster {ClusterKey} > Bucket {BucketName} > Create scope {ScopeName} success (exist)"
    )]
    public static partial void CreateScopeSucceededAlreadyExists(
        this ILogger logger,
        string clusterKey,
        string bucketName,
        string scopeName
    );

    [LoggerMessage(
        EventId = 5105,
        Level = LogLevel.Error,
        Message = "Cluster {ClusterKey} > Bucket {BucketName} > Create scope {ScopeName} failed"
    )]
    public static partial void CreateScopeFailed(
        this ILogger logger,
        Exception exception,
        string clusterKey,
        string bucketName,
        string scopeName
    );

    [LoggerMessage(
        EventId = 5106,
        Level = LogLevel.Information,
        Message = "Cluster {ClusterKey} > Bucket {BucketName} > Create ALL COLLECTIONS in scope {ScopeName} SUCCESS took {Elapsed}"
    )]
    public static partial void CreateAllCollectionsSucceeded(
        this ILogger logger,
        string clusterKey,
        string bucketName,
        string scopeName,
        TimeSpan elapsed
    );

    [LoggerMessage(
        EventId = 5107,
        Level = LogLevel.Information,
        Message = "Cluster {ClusterKey} > Bucket {BucketName} > Create collection {ScopeName}.{CollectionName} SUCCESS took {Elapsed}"
    )]
    public static partial void CreateCollectionSucceeded(
        this ILogger logger,
        string clusterKey,
        string bucketName,
        string scopeName,
        string collectionName,
        TimeSpan elapsed
    );

    [LoggerMessage(
        EventId = 5108,
        Level = LogLevel.Error,
        Message = "Cluster {ClusterKey} > Bucket {BucketName} > Create collection {ScopeName}.{CollectionName} FAILED took {Elapsed}"
    )]
    public static partial void CreateCollectionFailed(
        this ILogger logger,
        Exception exception,
        string clusterKey,
        string bucketName,
        string scopeName,
        string collectionName,
        TimeSpan elapsed
    );

    [LoggerMessage(
        EventId = 5109,
        Level = LogLevel.Information,
        Message = "Cluster {ClusterKey} > Bucket {BucketName} > Create secondary index on collection {ScopeName}.{CollectionName} IndexName={IndexName} SUCCESS took {Elapsed}"
    )]
    public static partial void CreateSecondaryIndexSucceeded(
        this ILogger logger,
        string clusterKey,
        string bucketName,
        string scopeName,
        string collectionName,
        string indexName,
        TimeSpan elapsed
    );

    [LoggerMessage(
        EventId = 5110,
        Level = LogLevel.Error,
        Message = "Cluster {ClusterKey} > Bucket {BucketName} > Create secondary index on collection {ScopeName}.{CollectionName} IndexName={IndexName} FAILED took {Elapsed}"
    )]
    public static partial void CreateSecondaryIndexFailed(
        this ILogger logger,
        Exception exception,
        string clusterKey,
        string bucketName,
        string scopeName,
        string collectionName,
        string indexName,
        TimeSpan elapsed
    );

    [LoggerMessage(
        EventId = 5111,
        Level = LogLevel.Information,
        Message = "Cluster {ClusterKey} > Bucket {BucketName} > Create primary index on collection {ScopeName}.{CollectionName} SUCCESS took {Elapsed}"
    )]
    public static partial void CreatePrimaryIndexSucceeded(
        this ILogger logger,
        string clusterKey,
        string bucketName,
        string scopeName,
        string collectionName,
        TimeSpan elapsed
    );

    [LoggerMessage(
        EventId = 5112,
        Level = LogLevel.Error,
        Message = "Cluster {ClusterKey} > Bucket {BucketName} > Failed to create primary index on collection {ScopeName}.{CollectionName} FAILED took {Elapsed}"
    )]
    public static partial void CreatePrimaryIndexFailed(
        this ILogger logger,
        Exception exception,
        string clusterKey,
        string bucketName,
        string scopeName,
        string collectionName,
        TimeSpan elapsed
    );
}
