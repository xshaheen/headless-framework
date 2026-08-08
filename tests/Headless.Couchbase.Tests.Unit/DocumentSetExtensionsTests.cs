// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase.Core.Exceptions.KeyValue;
using Couchbase.KeyValue;
using Couchbase.KeyValue.RangeScan;
using Couchbase.Linq;
using Headless.Couchbase.Context;
using Headless.Testing.Tests;

namespace Tests;

public sealed class DocumentSetExtensionsTests : TestBase
{
    [Fact]
    public async Task should_get_document_by_stringified_id()
    {
        var (set, collection) = _CreateSet();
        var result = Substitute.For<IGetResult>();
        var document = new TestDocument { Id = "42", Value = "found" };
        result.ContentAs<TestDocument>().Returns(document);
        collection.GetAsync("42", Arg.Any<GetOptions>()).Returns(result);

        var actual = await set.GetAsync<TestDocument, int>(42, AbortToken);

        actual.Should().BeSameAs(document);
        await collection.Received(1).GetAsync("42", Arg.Any<GetOptions>());
    }

    [Fact]
    public async Task should_return_null_when_document_is_missing()
    {
        var (set, collection) = _CreateSet();
        collection
            .GetAsync("missing", Arg.Any<GetOptions?>())
            .Returns(Task.FromException<IGetResult>(new DocumentNotFoundException()));

        var actual = await set.GetAsync<TestDocument, string>("missing", new GetOptions());

        actual.Should().BeNull();
    }

    [Fact]
    public async Task should_forward_exists_request_with_exact_options()
    {
        var (set, collection) = _CreateSet();
        var options = new ExistsOptions();

        await set.ExistsAsync<TestDocument, int>(42, options);

        await collection.Received(1).ExistsAsync("42", options);
    }

    [Fact]
    public async Task should_upsert_using_composite_entity_key()
    {
        var (set, collection) = _CreateSet<CompositeDocument>();
        var document = new CompositeDocument("tenant", 42);
        var options = new UpsertOptions();

        await set.UpsertAsync(document, options);

        await collection.Received(1).UpsertAsync("tenant:42", document, options);
    }

    [Fact]
    public async Task should_insert_using_entity_key()
    {
        var (set, collection) = _CreateSet();
        var document = new TestDocument { Id = "doc-1" };
        var options = new InsertOptions();

        await set.InsertAsync(document, options);

        await collection.Received(1).InsertAsync("doc-1", document, options);
    }

    [Fact]
    public async Task should_replace_using_entity_key()
    {
        var (set, collection) = _CreateSet();
        var document = new TestDocument { Id = "doc-1" };
        var options = new ReplaceOptions();

        await set.ReplaceAsync(document, options);

        await collection.Received(1).ReplaceAsync("doc-1", document, options);
    }

    [Fact]
    public async Task should_remove_stringified_id()
    {
        var (set, collection) = _CreateSet();
        var options = new RemoveOptions();

        await set.RemoveAsync<TestDocument, int>(42, options);

        await collection.Received(1).RemoveAsync("42", options);
    }

    [Fact]
    public async Task should_unlock_stringified_id_with_cas()
    {
        var (set, collection) = _CreateSet();
        var options = new UnlockOptions();

        await set.UnlockAsync<TestDocument, int>(42, 123UL, options);

        await collection.Received(1).UnlockAsync("42", 123UL, options);
    }

    [Fact]
    public async Task should_forward_touch_and_read_expiry_operations()
    {
        var (set, collection) = _CreateSet();
        var expiry = TimeSpan.FromMinutes(5);
        var touchOptions = new TouchOptions();
        var getAndTouchOptions = new GetAndTouchOptions();

        await set.TouchAsync<TestDocument, int>(42, expiry, touchOptions);
        await set.TouchWithCasAsync<TestDocument, int>(42, expiry, touchOptions);
        await set.GetAndTouchAsync<TestDocument, int>(42, expiry, getAndTouchOptions);

        await collection.Received(1).TouchAsync("42", expiry, touchOptions);
        await collection.Received(1).TouchWithCasAsync("42", expiry, touchOptions);
        await collection.Received(1).GetAndTouchAsync("42", expiry, getAndTouchOptions);
    }

    [Fact]
    public async Task should_forward_lock_and_replica_reads()
    {
        var (set, collection) = _CreateSet();
        var expiry = TimeSpan.FromSeconds(10);
        var lockOptions = new GetAndLockOptions();
        var anyReplicaOptions = new GetAnyReplicaOptions();
        var allReplicaOptions = new GetAllReplicasOptions();

        await set.GetAndLockAsync<TestDocument, int>(42, expiry, lockOptions);
        await set.GetAnyReplicaAsync<TestDocument, int>(42, anyReplicaOptions);
        _ = set.GetAllReplicas<TestDocument, int>(42, allReplicaOptions).ToList();

        await collection.Received(1).GetAndLockAsync("42", expiry, lockOptions);
        await collection.Received(1).GetAnyReplicaAsync("42", anyReplicaOptions);
        collection.Received(1).GetAllReplicasAsync("42", allReplicaOptions);
    }

    [Fact]
    public async Task should_forward_subdocument_reads_and_mutations()
    {
        var (set, collection) = _CreateSet();
        LookupInSpec[] lookupSpecs = [LookupInSpec.Get("profile.name")];
        MutateInSpec[] mutationSpecs = [MutateInSpec.Upsert("profile.active", true)];
        var lookupOptions = new LookupInOptions();
        var lookupAnyOptions = new LookupInAnyReplicaOptions();
        var lookupAllOptions = new LookupInAllReplicasOptions();
        var mutationOptions = new MutateInOptions();

        await set.LookupInAsync<TestDocument, int>(42, lookupSpecs, lookupOptions);
        await set.LookupInAnyReplicaAsync<TestDocument, int>(42, lookupSpecs, lookupAnyOptions);
        _ = set.LookupInAllReplicasAsync<TestDocument, int>(42, lookupSpecs, lookupAllOptions);
        await set.MutateInAsync<TestDocument, int>(42, mutationSpecs, mutationOptions);

        await collection.Received(1).LookupInAsync("42", lookupSpecs, lookupOptions);
        await collection.Received(1).LookupInAnyReplicaAsync("42", lookupSpecs, lookupAnyOptions);
        collection.Received(1).LookupInAllReplicasAsync("42", lookupSpecs, lookupAllOptions);
        await collection.Received(1).MutateInAsync("42", mutationSpecs, mutationOptions);
    }

    [Fact]
    public void should_return_collection_scan_stream()
    {
        var (set, collection) = _CreateSet();
        var scanType = Substitute.For<IScanType>();
        var options = new ScanOptions();
        var expected = Substitute.For<IAsyncEnumerable<IScanResult>>();
        collection.ScanAsync(scanType, options).Returns(expected);

        var actual = set.ScanAsync(scanType, options);

        actual.Should().BeSameAs(expected);
    }

    private static (IDocumentSet<TestDocument> Set, ICouchbaseCollection Collection) _CreateSet()
    {
        return _CreateSet<TestDocument>();
    }

    private static (IDocumentSet<T> Set, ICouchbaseCollection Collection) _CreateSet<T>()
    {
        var set = Substitute.For<IDocumentSet<T>>();
        var collection = Substitute.For<ICouchbaseCollection>();
        set.Collection.Returns(collection);

        return (set, collection);
    }

    public sealed record CompositeDocument(string Tenant, int Number) : Headless.Domain.IEntity
    {
        public IReadOnlyList<object> GetKeys() => [Tenant, Number];
    }
}
