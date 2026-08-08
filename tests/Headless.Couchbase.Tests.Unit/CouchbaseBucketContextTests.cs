// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase.KeyValue;
using Headless.Couchbase.Context;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class CouchbaseBucketContextTests : TestBase
{
    [Fact]
    public async Task should_honor_cancellation_before_starting_transaction()
    {
        var cluster = CouchbaseTestFactory.CreateCluster();
        var context = new TestBucketContext(
            CouchbaseTestFactory.CreateBucket(cluster),
            CouchbaseTestFactory.CreateTransactions(cluster),
            NullLogger<CouchbaseBucketContext>.Instance
        );
        var cancellationToken = new CancellationToken(canceled: true);
        var operationCalled = false;

        var act = () =>
            context.ExecuteTransactionAsync(
                _ =>
                {
                    operationCalled = true;
                    return Task.FromResult(true);
                },
                cancellationToken: cancellationToken
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
        operationCalled.Should().BeFalse();
    }

    [Fact]
    public void should_create_scope_and_collection_queryable()
    {
        var cluster = CouchbaseTestFactory.CreateCluster();
        var bucket = CouchbaseTestFactory.CreateBucket(cluster);
        var scope = Substitute.For<IScope>();
        var collection = Substitute.For<ICouchbaseCollection>();
        bucket.Name.Returns("app");
#pragma warning disable VSTHRD103 // Linq2Couchbase's Query API intentionally uses the synchronous SDK lookup.
        bucket.Scope("sales").Returns(scope);
#pragma warning restore VSTHRD103
        scope.Name.Returns("sales");
        scope.Bucket.Returns(bucket);
        scope.Collection("orders").Returns(collection);
        collection.Name.Returns("orders");
        collection.Scope.Returns(scope);
        var context = new TestBucketContext(
            bucket,
            CouchbaseTestFactory.CreateTransactions(cluster),
            NullLogger<CouchbaseBucketContext>.Instance
        );

        var query = context.Query<TestDocument>("sales", "orders");

        query.ElementType.Should().Be(typeof(TestDocument));
        _GetQueryKeyspace(query).Should().Be(("sales", "orders"));
    }

    private static (string Scope, string Collection) _GetQueryKeyspace<T>(IQueryable<T> query)
    {
        object instance = query;
        var type = instance.GetType();
        var scope = type.GetProperty("ScopeName")?.GetValue(instance);
        var collection = type.GetProperty("CollectionName")?.GetValue(instance);

        return (scope.Should().BeOfType<string>().Subject, collection.Should().BeOfType<string>().Subject);
    }
}
