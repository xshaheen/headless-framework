// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Couchbase;
using Couchbase.Management.Eventing;
using Couchbase.Query;
using Humanizer;

namespace Framework.Orm.Couchbase.Clusters;

public static class CouchbaseEventingFunctionsSeeder
{
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

        await cluster.EventingFunctions.UpsertFunctionAsync(function, upsertOptions);
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

public readonly record struct CouchbaseKeyspace(string Bucket, string Scope, string Collection);
