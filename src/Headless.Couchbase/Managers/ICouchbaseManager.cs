// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Couchbase;
using Couchbase.Core.Exceptions;
using Couchbase.KeyValue;
using Couchbase.Management.Collections;
using Couchbase.Management.Query;
using Headless.Checks;
using Headless.Couchbase.Clusters;
using Humanizer;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Headless.Couchbase.Managers;

/// <summary>
/// Provides idempotent scope, collection, and index management operations against a Couchbase cluster.
/// All mutating operations are guarded by a Polly retry pipeline with linear back-off and a 10-second
/// overall timeout; transient failures are retried while idempotent exceptions (scope/collection/index
/// already exists) are treated as success.
/// </summary>
[PublicAPI]
public interface ICouchbaseManager
{
    /// <summary>
    /// Creates the named scope in the bucket if it does not already exist.
    /// </summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="bucketName">The bucket in which to create the scope.</param>
    /// <param name="scopeName">The name of the scope to create.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>
    /// <see cref="CreateScopeStatus.Exist"/> when the scope already exists,
    /// <see cref="CreateScopeStatus.Success"/> when it was created, or
    /// <see cref="CreateScopeStatus.Failed"/> when all retries were exhausted.
    /// </returns>
    Task<CreateScopeStatus> CreateScopeAsync(
        string clusterKey,
        string bucketName,
        string scopeName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates any collections in <paramref name="collections"/> that do not yet exist within the scope,
    /// then ensures each has a primary index.
    /// </summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="bucketName">The bucket containing the scope.</param>
    /// <param name="scopeName">The scope in which to create collections.</param>
    /// <param name="collections">The set of collection names to ensure exist.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    Task CreateCollectionsAsync(
        string clusterKey,
        string bucketName,
        string scopeName,
        HashSet<string> collections,
        CancellationToken cancellationToken = default
    );

    /// <summary>Creates a named secondary index on a collection if it does not already exist.</summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="bucketName">The bucket containing the collection.</param>
    /// <param name="scopeName">The scope containing the collection.</param>
    /// <param name="collectionName">The collection to index.</param>
    /// <param name="indexName">The name to assign to the index.</param>
    /// <param name="fields">The fields to include in the index.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    Task CreateSecondaryIndexAsync(
        string clusterKey,
        string bucketName,
        string scopeName,
        string collectionName,
        string indexName,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default
    );

    /// <summary>Triggers the build of all deferred indexes on the bucket.</summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="bucketName">The bucket whose deferred indexes should be built.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    Task BuildDeferredIndexesAsync(string clusterKey, string bucketName, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a <c>CreateScopeAsync</c> call.</summary>
/// <remarks>Additional members may be added in future versions; handle unrecognized values defensively.</remarks>
[PublicAPI]
public enum CreateScopeStatus
{
    /// <summary>Default, unassigned outcome. Not returned by <c>CreateScopeAsync</c>; present as the zero sentinel.</summary>
    Unknown = 0,

    /// <summary>The scope already existed; no action was taken.</summary>
    Exist = 1,

    /// <summary>The scope was successfully created.</summary>
    Success = 2,

    /// <summary>The operation failed after all retries were exhausted.</summary>
    Failed = 3,
}

/// <summary>Default <see cref="ICouchbaseManager"/> implementation.</summary>
[PublicAPI]
public sealed class CouchbaseManager : ICouchbaseManager
{
    private readonly ResiliencePipeline _retryPipeline;
    private readonly ICouchbaseClustersProvider _clustersProvider;
    private readonly ILogger<CouchbaseManager> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="CouchbaseManager"/> with a Polly retry pipeline
    /// (linear back-off, 500 ms base delay with jitter, 10-second overall timeout).
    /// </summary>
    /// <param name="clustersProvider">Provides access to registered Couchbase clusters.</param>
    /// <param name="logger">Logger for operation lifecycle events.</param>
    /// <param name="timeProvider">
    /// Optional time provider used by the Polly pipeline; defaults to <see cref="TimeProvider.System"/>.
    /// </param>
    public CouchbaseManager(
        ICouchbaseClustersProvider clustersProvider,
        ILogger<CouchbaseManager> logger,
        TimeProvider? timeProvider = null
    )
    {
        _clustersProvider = clustersProvider;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

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

        _retryPipeline = new ResiliencePipelineBuilder { TimeProvider = _timeProvider }
            .AddRetry(retryStrategyOptions)
            .AddTimeout(10.Seconds())
            .Build();
    }

    /// <inheritdoc/>
    public async Task BuildDeferredIndexesAsync(
        string clusterKey,
        string bucketName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(clusterKey);
        Argument.IsNotNull(bucketName);

        var timestamp = Stopwatch.GetTimestamp();
        var (cluster, _) = await _clustersProvider.GetClusterAsync(clusterKey, cancellationToken).ConfigureAwait(false);

        await _retryPipeline
            .ExecuteAsync(
                static async (state, token) =>
                {
                    await state
                        .cluster.QueryIndexes.BuildDeferredIndexesAsync(
                            state.bucketName,
                            options => options.CancellationToken(token)
                        )
                        .ConfigureAwait(false);
                },
                (cluster, bucketName),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.BuildDeferredIndexesSucceeded(clusterKey, bucketName, Stopwatch.GetElapsedTime(timestamp));
        }
    }

    /// <inheritdoc/>
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

        var bucket = await _GetBucketAsync(clusterKey, bucketName, cancellationToken).ConfigureAwait(false);
        var bucketScopes = await _GetBucketScopeSpecsAsync(clusterKey, bucket).ConfigureAwait(false);

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
            return await _retryPipeline
                .ExecuteAsync(
                    static async (state, token) =>
                    {
                        var (@this, clusterKey, scopeName, bucket) = state;

                        try
                        {
                            await bucket
                                .Collections.CreateScopeAsync(scopeName, options => options.CancellationToken(token))
                                .ConfigureAwait(false);

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
                )
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.CreateScopeFailed(e, clusterKey, bucketName, scopeName);

            return CreateScopeStatus.Failed;
        }
    }

    /// <inheritdoc/>
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
        var bucket = await _GetBucketAsync(clusterKey, bucketName, cancellationToken).ConfigureAwait(false);
        var (scope, scopeCollections) = await _GetScopeAsync(clusterKey, bucket, scopeName).ConfigureAwait(false);

        await Parallel
            .ForEachAsync(
                collections,
                cancellationToken,
                async (collectionName, token) =>
                {
                    if (scopeCollections.Contains(collectionName))
                    {
                        var collection = await scope.CollectionAsync(collectionName).ConfigureAwait(false);

                        if (!await _HasPrimaryIndexAsync(collection, token).ConfigureAwait(false))
                        {
                            await _CreatePrimaryIndexOnCollectionAsync(clusterKey, collection).ConfigureAwait(false);
                        }

                        return;
                    }

                    await _CreateCollectionAsync(clusterKey, bucket, scope, collectionName, token)
                        .ConfigureAwait(false);
                    await _timeProvider.Delay(50.Milliseconds(), token).ConfigureAwait(false);
                    await _CreatePrimaryIndexOnCollectionAsync(
                            clusterKey,
                            await scope.CollectionAsync(collectionName).ConfigureAwait(false)
                        )
                        .ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

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
            await _retryPipeline
                .ExecuteAsync(
                    static async (state, token) =>
                    {
                        var (bucket, scope, collectionName) = state;

                        try
                        {
                            await bucket
                                .Collections.CreateCollectionAsync(
                                    scope.Name,
                                    collectionName,
                                    CreateCollectionSettings.Default,
                                    CreateCollectionOptions.Default.CancellationToken(token)
                                )
                                .ConfigureAwait(false);
                        }
                        catch (CollectionExistsException)
                        {
                            // Ignore if a collection already exists it's same as success
                        }
                    },
                    (bucket, scope, collectionName),
                    token
                )
                .ConfigureAwait(false);

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

    /// <inheritdoc/>
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
        var bucket = await _GetBucketAsync(clusterKey, bucketName, cancellationToken).ConfigureAwait(false);
        var scope = await bucket.ScopeAsync(scopeName).ConfigureAwait(false);
        var collection = await scope.CollectionAsync(collectionName).ConfigureAwait(false);

        try
        {
            await _retryPipeline
                .ExecuteAsync(
                    static async (state, token) =>
                    {
                        var (collection, indexName, fields) = state;

                        var options = CreateQueryIndexOptions
                            .Default.IgnoreIfExists(ignoreIfExists: true)
                            .Deferred(deferred: false)
                            .Timeout(5.Seconds())
                            .CancellationToken(token);

                        await collection
                            .QueryIndexes.CreateIndexAsync(indexName, fields, options)
                            .ConfigureAwait(false);
                    },
                    (collection, indexName, fields),
                    cancellationToken
                )
                .ConfigureAwait(false);

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
        var indexes = await collection
            .QueryIndexes.GetAllIndexesAsync(GetAllQueryIndexOptions.Default.CancellationToken(cancellationToken))
            .ConfigureAwait(false);

        return indexes.Any(index =>
            index.IsPrimary || string.Equals(index.Name, "#primary", StringComparison.OrdinalIgnoreCase)
        );
    }

    private async Task _CreatePrimaryIndexOnCollectionAsync(string clusterKey, ICouchbaseCollection collection)
    {
        var timestamp = Stopwatch.GetTimestamp();

        try
        {
            await _retryPipeline
                .ExecuteAsync(
                    static async (collection, token) =>
                    {
                        var options = CreatePrimaryQueryIndexOptions
                            .Default.IndexName("#primary")
                            .IgnoreIfExists(ignoreIfExists: true)
                            .Timeout(5.Seconds())
                            .Deferred(deferred: false)
                            .CancellationToken(token);

                        await collection.QueryIndexes.CreatePrimaryIndexAsync(options).ConfigureAwait(false);
                    },
                    collection
                )
                .ConfigureAwait(false);

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

    private async Task<ICluster> _GetClusterAsync(string clusterKey, CancellationToken cancellationToken)
    {
        var (cluster, _) = await _clustersProvider.GetClusterAsync(clusterKey, cancellationToken).ConfigureAwait(false);

        return cluster;
    }

    private async Task<IBucket> _GetBucketAsync(
        string clusterKey,
        string bucketName,
        CancellationToken cancellationToken
    )
    {
        var cluster = await _GetClusterAsync(clusterKey, cancellationToken).ConfigureAwait(false);
        var bucket = await cluster.BucketAsync(bucketName).ConfigureAwait(false);

        return bucket;
    }

    private async Task<(IScope Scope, IReadOnlySet<string> Collections)> _GetScopeAsync(
        string clusterKey,
        IBucket bucket,
        string scopeName
    )
    {
        var scopeSpecList = await _GetBucketScopeSpecsAsync(clusterKey, bucket).ConfigureAwait(false);
        var scopeCollections = scopeSpecList[scopeName];

        // Note: `ScopeAsync` method is cached by the SDK
        var scope = await bucket.ScopeAsync(scopeName).ConfigureAwait(false);

        return (scope, scopeCollections);
    }

    private readonly ConcurrentDictionary<string, FrozenDictionary<string, FrozenSet<string>>> _scopesCache = [];
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

    private async Task<FrozenDictionary<string, FrozenSet<string>>> _GetBucketScopeSpecsAsync(
        string clusterKey,
        IBucket bucket
    )
    {
        var key = _GetScopesCacheKey(clusterKey, bucket.Name);

        if (_scopesCache.TryGetValue(key, out var scopes))
        {
            return scopes;
        }

        var scopesEnumerable = await bucket.Collections.GetAllScopesAsync().ConfigureAwait(false);
        var mapped = scopesEnumerable.ToFrozenDictionary(
            x => x.Name,
            x => x.Collections.Select(y => y.Name).ToFrozenSet(StringComparer.Ordinal),
            StringComparer.Ordinal
        );

        return _scopesCache.GetOrAdd(key, mapped);
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
