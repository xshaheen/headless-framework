// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Couchbase;
using Couchbase.Management.Eventing;
using Couchbase.Query;
using Humanizer;

namespace Headless.Couchbase.Clusters;

/// <summary>
/// Extension method for deploying or updating a Couchbase Eventing function on a cluster.
/// </summary>
public static class CouchbaseEventingFunctionsSeeder
{
    /// <summary>
    /// Creates or updates the named Eventing function on the cluster, configuring its source and
    /// metadata keyspaces, bucket binding aliases, worker count, and deploying it immediately.
    /// </summary>
    /// <param name="cluster">The Couchbase cluster to deploy to.</param>
    /// <param name="dataKeyspace">The bucket/scope/collection that acts as the event source.</param>
    /// <param name="eventsKeyspace">The bucket/scope/collection used for function metadata storage.</param>
    /// <param name="aliases">Named read-only bucket aliases available to the function's JavaScript code.</param>
    /// <param name="functionName">The name to assign to the Eventing function.</param>
    /// <param name="javaScriptCode">The JavaScript source code of the function.</param>
    /// <param name="workers">Number of worker threads. Defaults to 1.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    public static async Task UpsertFunctionAsync(
        this ICluster cluster,
        CouchbaseKeyspace dataKeyspace,
        CouchbaseKeyspace eventsKeyspace,
        Dictionary<string, CouchbaseKeyspace> aliases,
        string functionName,
        string javaScriptCode,
        int workers = 1,
        CancellationToken cancellationToken = default
    )
    {
        var metaDataKeyspace = _CreateKeyspace(eventsKeyspace.Bucket, eventsKeyspace.Scope, eventsKeyspace.Collection);
        var sourceKeyspace = _CreateKeyspace(dataKeyspace.Bucket, dataKeyspace.Scope, dataKeyspace.Collection);

        var functionDeploymentConfig = new DeploymentConfig
        {
            SourceBucket = sourceKeyspace.Bucket,
            SourceScope = sourceKeyspace.Scope,
            SourceCollection = sourceKeyspace.Collection,
            MetadataBucket = metaDataKeyspace.Bucket,
            MetadataScope = metaDataKeyspace.Scope,
            MetadataCollection = metaDataKeyspace.Collection,
            BucketBindings = aliases.Select(alias => _CreateBucketBinding(alias.Key, alias.Value)).ToList(),
        };

        var function = new EventingFunction
        {
            Name = functionName,
            Code = javaScriptCode,
            EnforceSchema = false,
            SourceKeySpace = sourceKeyspace,
            MetaDataKeySpace = metaDataKeyspace,
            Settings = new EventingFunctionSettings
            {
                ExecutionTimeout = 1.Minutes(),
                DcpStreamBoundary = EventingFunctionDcpBoundary.Everything,
                DeploymentStatus = EventingFunctionDeploymentStatus.Deployed,
                ProcessingStatus = EventingFunctionProcessingStatus.Running,
                QueryConsistency = QueryScanConsistency.NotBounded,
                LanguageCompatibility = EventingFunctionLanguageCompatibility.Version_6_6_2,
                LogLevel = EventingFunctionLogLevel.Info,
                UserPrefix = "eventing",
                WorkerCount = workers,
                TimerContextSize = 1024,
            },
        };

        _SetDeploymentConfig(function, functionDeploymentConfig);

        var upsertOptions = new UpsertFunctionOptions { Timeout = 1.Minutes(), Token = cancellationToken };

        await cluster.EventingFunctions.UpsertFunctionAsync(function, upsertOptions).ConfigureAwait(false);
    }

    #region Helpers

    private static EventingFunctionBucketBinding _CreateBucketBinding(string alias, CouchbaseKeyspace keyspace)
    {
        var binding = new EventingFunctionBucketBinding
        {
            Alias = alias,
            Access = EventingFunctionBucketAccess.ReadOnly,
        };

        _SetBucketName(binding, keyspace.Bucket);
        _SetScopeName(binding, keyspace.Scope);
        _SetCollectionName(binding, keyspace.Collection);

        return binding;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern EventingFunctionKeyspace _CreateKeyspace(string bucket, string scope, string collection);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_DeploymentConfig")]
    private static extern void _SetDeploymentConfig(EventingFunction @this, DeploymentConfig deploymentConfig);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_BucketName")]
    private static extern void _SetBucketName(EventingFunctionBucketBinding @this, string value);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_ScopeName")]
    private static extern void _SetScopeName(EventingFunctionBucketBinding @this, string value);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_CollectionName")]
    private static extern void _SetCollectionName(EventingFunctionBucketBinding @this, string value);

    #endregion
}

/// <summary>
/// Identifies a Couchbase storage location by bucket, scope, and collection name.
/// </summary>
public readonly record struct CouchbaseKeyspace(string Bucket, string Scope, string Collection);
