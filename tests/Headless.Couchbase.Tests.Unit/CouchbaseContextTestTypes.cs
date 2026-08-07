// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using Couchbase;
using Couchbase.Linq;
using Couchbase.Transactions;
using Couchbase.Transactions.Config;
using Headless.Couchbase.Context;
using Headless.Domain;
using Microsoft.Extensions.Logging;

namespace Tests;

internal sealed class TestBucketContext(
    IBucket bucket,
    Transactions transactions,
    ILogger<CouchbaseBucketContext> logger
) : CouchbaseBucketContext(bucket, transactions, logger)
{
    [CouchbaseCollection("sales", "orders")]
    public IDocumentSet<TestDocument> Orders { get; set; } = null!;

    [CouchbaseCollection("inventory", "products")]
    public IDocumentSet<TestDocument> Products { get; set; } = null!;

    public string? IgnoredProperty { get; set; }
}

internal sealed class MissingAttributeBucketContext(
    IBucket bucket,
    Transactions transactions,
    ILogger<CouchbaseBucketContext> logger
) : CouchbaseBucketContext(bucket, transactions, logger)
{
    public IDocumentSet<TestDocument> Documents { get; set; } = null!;
}

internal sealed class InvalidAttributeBucketContext(
    IBucket bucket,
    Transactions transactions,
    ILogger<CouchbaseBucketContext> logger
) : CouchbaseBucketContext(bucket, transactions, logger)
{
    [CouchbaseCollection(" ", "documents")]
    public IDocumentSet<TestDocument> Documents { get; set; } = null!;
}

public sealed class TestDocument : Entity<string>
{
    public string? Value { get; init; }
}

internal static class CouchbaseTestFactory
{
    private static readonly Type _ClusterContextType =
        typeof(ICluster).Assembly.GetType("Couchbase.Core.ClusterContext")
        ?? throw new InvalidOperationException("Could not find ClusterContext type.");

    private static readonly MethodInfo _AddClusterService = typeof(ClusterOptionsExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method =>
            method.Name == nameof(ClusterOptionsExtensions.AddClusterService)
            && method.IsGenericMethodDefinition
            && method.GetGenericArguments().Length == 1
            && method.GetParameters().Length == 2
            && method.GetParameters()[1].ParameterType.IsGenericParameter
        );

    private static readonly MethodInfo _BuildServiceProvider =
        typeof(ClusterOptions).GetMethod(
            "BuildServiceProvider",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        ) ?? throw new InvalidOperationException("Could not find ClusterOptions.BuildServiceProvider.");

    public static ICluster CreateCluster()
    {
        var clusterContext = RuntimeHelpers.GetUninitializedObject(_ClusterContextType);
        var options = new ClusterOptions();
        _ = _AddClusterService.MakeGenericMethod(_ClusterContextType).Invoke(null, [options, clusterContext]);
        options.AddLinq();
        var serviceProvider = _BuildServiceProvider.Invoke(options, null) as IServiceProvider;
        serviceProvider.Should().NotBeNull();
        var cluster = Substitute.For<ICluster>();
        cluster.ClusterServices.Returns(serviceProvider!);

        return cluster;
    }

    public static IBucket CreateBucket(ICluster cluster)
    {
        var bucket = Substitute.For<IBucket>();
        bucket.Cluster.Returns(cluster);

        return bucket;
    }

    public static Transactions CreateTransactions(ICluster cluster)
    {
        var config = TransactionConfigBuilder
            .Create()
            .CleanupLostAttempts(cleanupLostAttempts: false)
            .CleanupClientAttempts(cleanupClientAttempts: false);

        return Transactions.Create(cluster, config);
    }
}
