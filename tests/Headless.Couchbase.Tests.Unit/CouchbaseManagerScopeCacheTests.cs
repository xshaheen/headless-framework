// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase;
using Couchbase.KeyValue;
using Couchbase.Management.Collections;
using Couchbase.Management.Query;
using Headless.Couchbase.Clusters;
using Headless.Couchbase.Managers;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class CouchbaseManagerScopeCacheTests : TestBase
{
    [Fact]
    public async Task should_return_exist_without_mutating_when_scope_is_already_present()
    {
        var fixture = _CreateFixture([_Scope("orders", "existing")]);

        var result = await fixture.Manager.CreateScopeAsync("primary", "app", "orders", AbortToken);

        result.Should().Be(CreateScopeStatus.Exist);
        await fixture.Collections.DidNotReceiveWithAnyArgs().CreateScopeAsync((string)default!, default);
    }

    [Fact]
    public async Task should_cache_scope_metadata_between_read_only_checks()
    {
        var fixture = _CreateFixture([_Scope("orders", "existing")]);

        await fixture.Manager.CreateScopeAsync("primary", "app", "orders", AbortToken);
        await fixture.Manager.CreateScopeAsync("primary", "app", "orders", AbortToken);

        await fixture.Collections.Received(1).GetAllScopesAsync(Arg.Any<GetAllScopesOptions?>());
    }

    [Fact]
    public async Task should_clear_scope_cache_after_successful_creation()
    {
        var fixture = _CreateFixture([]);
        fixture
            .Collections.GetAllScopesAsync(Arg.Any<GetAllScopesOptions?>())
            .Returns(
                Task.FromResult<IEnumerable<ScopeSpec>>([]),
                Task.FromResult<IEnumerable<ScopeSpec>>([_Scope("orders")])
            );

        var created = await fixture.Manager.CreateScopeAsync("primary", "app", "orders", AbortToken);
        var reread = await fixture.Manager.CreateScopeAsync("primary", "app", "orders", AbortToken);

        created.Should().Be(CreateScopeStatus.Success);
        reread.Should().Be(CreateScopeStatus.Exist);
        await fixture.Collections.Received(1).CreateScopeAsync("orders", Arg.Any<CreateScopeOptions?>());
        await fixture.Collections.Received(2).GetAllScopesAsync(Arg.Any<GetAllScopesOptions?>());
    }

    [Fact]
    public async Task should_return_failed_after_scope_creation_retries_are_exhausted()
    {
        var fixture = _CreateFixture([]);
        fixture
            .Collections.CreateScopeAsync("orders", Arg.Any<CreateScopeOptions?>())
            .Returns(_ => throw new InvalidOperationException("cluster unavailable"));

        var result = await fixture.Manager.CreateScopeAsync("primary", "app", "orders", AbortToken);

        result.Should().Be(CreateScopeStatus.Failed);
        await fixture.Collections.Received(2).CreateScopeAsync("orders", Arg.Any<CreateScopeOptions?>());
    }

    [Fact]
    public async Task should_create_primary_index_for_existing_collection_when_missing()
    {
        var fixture = _CreateFixture([_Scope("orders", "receipts")]);
        var scope = Substitute.For<IScope>();
        var collection = Substitute.For<ICouchbaseCollection>();
        var indexes = Substitute.For<ICollectionQueryIndexManager>();
        fixture.Bucket.ScopeAsync("orders").Returns(scope);
        scope.CollectionAsync("receipts").Returns(collection);
        collection.QueryIndexes.Returns(indexes);
        indexes
            .GetAllIndexesAsync(Arg.Any<GetAllQueryIndexOptions>())
            .Returns(Task.FromResult<IEnumerable<QueryIndex>>([]));

        await fixture.Manager.CreateCollectionsAsync(
            "primary",
            "app",
            "orders",
            new HashSet<string>(StringComparer.Ordinal) { "receipts" },
            AbortToken
        );

        await fixture
            .Collections.DidNotReceive()
            .CreateCollectionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CreateCollectionSettings>(),
                Arg.Any<CreateCollectionOptions>()
            );
        await indexes.Received(1).CreatePrimaryIndexAsync(Arg.Any<CreatePrimaryQueryIndexOptions>());
    }

    [Fact]
    public async Task should_create_missing_collection_before_its_primary_index()
    {
        var fixture = _CreateFixture([_Scope("orders")]);
        var scope = Substitute.For<IScope>();
        var collection = Substitute.For<ICouchbaseCollection>();
        var indexes = Substitute.For<ICollectionQueryIndexManager>();
        scope.Name.Returns("orders");
        fixture.Bucket.ScopeAsync("orders").Returns(scope);
        scope.CollectionAsync("receipts").Returns(collection);
        collection.QueryIndexes.Returns(indexes);

        await fixture.Manager.CreateCollectionsAsync(
            "primary",
            "app",
            "orders",
            new HashSet<string>(StringComparer.Ordinal) { "receipts" },
            AbortToken
        );

        Received.InOrder(() =>
        {
            _ = fixture.Collections.CreateCollectionAsync(
                "orders",
                "receipts",
                Arg.Any<CreateCollectionSettings>(),
                Arg.Any<CreateCollectionOptions>()
            );
            _ = indexes.CreatePrimaryIndexAsync(Arg.Any<CreatePrimaryQueryIndexOptions>());
        });
    }

    [Fact]
    public async Task should_create_secondary_index_with_requested_fields()
    {
        var fixture = _CreateFixture([]);
        var scope = Substitute.For<IScope>();
        var collection = Substitute.For<ICouchbaseCollection>();
        var indexes = Substitute.For<ICollectionQueryIndexManager>();
        fixture.Bucket.ScopeAsync("orders").Returns(scope);
        scope.CollectionAsync("receipts").Returns(collection);
        collection.QueryIndexes.Returns(indexes);
        string[] fields = ["customerId", "createdAt"];

        await fixture.Manager.CreateSecondaryIndexAsync(
            "primary",
            "app",
            "orders",
            "receipts",
            "ix_customer_created",
            fields,
            AbortToken
        );

        await indexes
            .Received(1)
            .CreateIndexAsync(
                "ix_customer_created",
                Arg.Is<IEnumerable<string>>(actual => actual.SequenceEqual(fields)),
                Arg.Any<CreateQueryIndexOptions>()
            );
    }

    [Fact]
    public async Task should_build_deferred_indexes_on_requested_bucket()
    {
        var fixture = _CreateFixture([]);
        var indexes = Substitute.For<IQueryIndexManager>();
        fixture.Cluster.QueryIndexes.Returns(indexes);

        await fixture.Manager.BuildDeferredIndexesAsync("primary", "app", AbortToken);

        await indexes.Received(1).BuildDeferredIndexesAsync("app", Arg.Any<BuildDeferredQueryIndexOptions>());
    }

    [Fact]
    public async Task should_propagate_cancellation_before_manager_operation()
    {
        var cancellationToken = new CancellationToken(canceled: true);
        var fixture = _CreateFixture([]);

        var act = () => fixture.Manager.BuildDeferredIndexesAsync("primary", "app", cancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ManagerFixture _CreateFixture(IEnumerable<ScopeSpec> scopes)
    {
        var clusters = Substitute.For<ICouchbaseClustersProvider>();
        var cluster = Substitute.For<ICluster>();
        var bucket = Substitute.For<IBucket>();
        var collections = Substitute.For<ICouchbaseCollectionManager>();
        bucket.Name.Returns("app");
        bucket.Collections.Returns(collections);
        cluster.BucketAsync("app").Returns(bucket);
        clusters
            .GetClusterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CouchbaseClusterConnection { Cluster = cluster, Transactions = null! });
        collections.GetAllScopesAsync(Arg.Any<GetAllScopesOptions?>()).Returns(Task.FromResult(scopes));

        var manager = new CouchbaseManager(
            clusters,
            Options.Create(
                new CouchbaseManagerOptions
                {
                    MaxRetries = 1,
                    RetryDelay = TimeSpan.Zero,
                    Timeout = TimeSpan.FromSeconds(5),
                }
            ),
            NullLogger<CouchbaseManager>.Instance
        );

        return new(manager, cluster, bucket, collections);
    }

    private static ScopeSpec _Scope(string scope, params string[] collections)
    {
        return new(scope) { Collections = [.. collections.Select(name => new CollectionSpec(scope, name))] };
    }

    private sealed record ManagerFixture(
        CouchbaseManager Manager,
        ICluster Cluster,
        IBucket Bucket,
        ICouchbaseCollectionManager Collections
    );
}
