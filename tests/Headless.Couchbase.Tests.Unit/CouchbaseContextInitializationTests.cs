// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase;
using Couchbase.Linq;
using Couchbase.Transactions;
using Headless.Couchbase.Clusters;
using Headless.Couchbase.Context;
using Headless.Couchbase.ContextProvider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class CouchbaseContextInitializationTests : TestBase
{
    [Fact]
    public void should_initialize_declared_scope_and_collection_mappings()
    {
        var (provider, bucket, transactions) = _CreateDependencies();
        using var providerLifetime = provider;

        var context = CouchbaseBucketContextInitializer.Initialize<TestBucketContext>(
            provider,
            bucket,
            transactions,
            defaultScopeName: null
        );

        _GetMapping(context.Orders).Should().Be(("sales", "orders"));
        _GetMapping(context.Products).Should().Be(("inventory", "products"));
    }

    [Fact]
    public void should_flatten_declared_mappings_into_default_scope()
    {
        var (provider, bucket, transactions) = _CreateDependencies();
        using var providerLifetime = provider;

        var context = CouchbaseBucketContextInitializer.Initialize<TestBucketContext>(
            provider,
            bucket,
            transactions,
            defaultScopeName: "tenant-42"
        );

        _GetMapping(context.Orders).Should().Be(("tenant-42", "sales_orders"));
        _GetMapping(context.Products).Should().Be(("tenant-42", "inventory_products"));
    }

    [Fact]
    public void should_reject_document_set_without_collection_attribute()
    {
        var (provider, bucket, transactions) = _CreateDependencies();
        using var providerLifetime = provider;
        var act = () =>
            CouchbaseBucketContextInitializer.Initialize<MissingAttributeBucketContext>(
                provider,
                bucket,
                transactions,
                defaultScopeName: null
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Missing CouchbaseCollectionAttribute on Documents*");
    }

    [Fact]
    public void should_reject_blank_collection_scope()
    {
        var (provider, bucket, transactions) = _CreateDependencies();
        using var providerLifetime = provider;
        var act = () =>
            CouchbaseBucketContextInitializer.Initialize<InvalidAttributeBucketContext>(
                provider,
                bucket,
                transactions,
                defaultScopeName: null
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*Invalid Scope on Documents*");
    }

    [Fact]
    public async Task should_open_bucket_and_initialize_context_through_provider()
    {
        var clusterProvider = Substitute.For<ICouchbaseClustersProvider>();
        var cluster = CouchbaseTestFactory.CreateCluster();
        var bucket = CouchbaseTestFactory.CreateBucket(cluster);
        var transactions = CouchbaseTestFactory.CreateTransactions(cluster);
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<CouchbaseBucketContext>>(NullLogger<CouchbaseBucketContext>.Instance);
        await using var serviceProvider = services.BuildServiceProvider();
        clusterProvider
            .GetClusterAsync("primary", AbortToken)
            .Returns(new CouchbaseClusterConnection { Cluster = cluster, Transactions = transactions });
        cluster.BucketAsync("app").Returns(bucket);
        var sut = new BucketContextProvider(clusterProvider, serviceProvider);

        var context = await sut.GetAsync<TestBucketContext>("primary", "app", "tenant", AbortToken);

        _GetMapping(context.Orders).Should().Be(("tenant", "sales_orders"));
        await cluster.Received(1).BucketAsync("app");
    }

    [Fact]
    public async Task should_not_open_bucket_when_caller_is_cancelled()
    {
        var cancellationToken = new CancellationToken(canceled: true);
        var clusterProvider = Substitute.For<ICouchbaseClustersProvider>();
        var cluster = CouchbaseTestFactory.CreateCluster();
        var transactions = CouchbaseTestFactory.CreateTransactions(cluster);
        clusterProvider
            .GetClusterAsync("primary", cancellationToken)
            .Returns(new CouchbaseClusterConnection { Cluster = cluster, Transactions = transactions });
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var sut = new BucketContextProvider(clusterProvider, serviceProvider);

        var act = () => sut.GetAsync<TestBucketContext>("primary", "app", null, cancellationToken).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
        await cluster.DidNotReceiveWithAnyArgs().BucketAsync(default!);
    }

    private static (ServiceProvider Provider, IBucket Bucket, Transactions Transactions) _CreateDependencies()
    {
        var cluster = CouchbaseTestFactory.CreateCluster();
        var bucket = CouchbaseTestFactory.CreateBucket(cluster);
        var transactions = CouchbaseTestFactory.CreateTransactions(cluster);
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<CouchbaseBucketContext>>(NullLogger<CouchbaseBucketContext>.Instance);

        return (services.BuildServiceProvider(), bucket, transactions);
    }

    private static (string Scope, string Collection) _GetMapping<T>(IDocumentSet<T> documentSet)
    {
        object instance = documentSet;
        var type = instance.GetType();
        var scope = type.GetProperty("ScopeName")?.GetValue(instance);
        var collection = type.GetProperty("CollectionName")?.GetValue(instance);

        return (scope.Should().BeOfType<string>().Subject, collection.Should().BeOfType<string>().Subject);
    }
}
