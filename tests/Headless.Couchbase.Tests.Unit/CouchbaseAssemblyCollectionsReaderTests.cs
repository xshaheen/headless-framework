// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase;
using Headless.Couchbase.Context;
using Headless.Couchbase.Managers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class CouchbaseAssemblyCollectionsReaderTests
{
    private readonly CouchbaseAssemblyCollectionsReader _sut = new();

    [Fact]
    public void should_discover_annotated_document_sets_from_assemblies()
    {
        var collections = _sut.ReadCollections([typeof(TestBucketContext).Assembly]).ToList();

        collections.Should().Contain(new ScopeCollection("sales", "orders"));
        collections.Should().Contain(new ScopeCollection("inventory", "products"));
        collections.Should().NotContain(x => x.Collection == nameof(TestBucketContext.IgnoredProperty));
    }

    [Fact]
    public void should_discover_collections_from_live_context_types()
    {
        var cluster = CouchbaseTestFactory.CreateCluster();
        var context = new TestBucketContext(
            CouchbaseTestFactory.CreateBucket(cluster),
            CouchbaseTestFactory.CreateTransactions(cluster),
            NullLogger<CouchbaseBucketContext>.Instance
        );

        var collections = _sut.ReadCollections([context]).ToList();

        collections
            .Should()
            .BeEquivalentTo([new ScopeCollection("sales", "orders"), new ScopeCollection("inventory", "products")]);
    }

    [Fact]
    public void should_filter_loaded_assemblies_by_prefix()
    {
        var collections = _sut.ReadCollections("Headless.Couchbase.Tests.Unit").ToList();

        collections.Should().Contain(new ScopeCollection("sales", "orders"));
        _sut.ReadCollections("missing.assembly.prefix").Should().BeEmpty();
    }
}
