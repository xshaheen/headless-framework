// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Reflection;
using Couchbase;
using Couchbase.Management.Collections;
using Headless.Couchbase.Clusters;
using Headless.Couchbase.Managers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class CouchbaseManagerScopeCacheTests
{
    [Fact]
    public async Task should_cache_scope_specs_as_frozen_read_only_metadata()
    {
        var manager = new CouchbaseManager(
            Substitute.For<ICouchbaseClustersProvider>(),
            NullLogger<CouchbaseManager>.Instance
        );
        var bucket = Substitute.For<IBucket>();
        var collectionManager = Substitute.For<ICouchbaseCollectionManager>();

        bucket.Name.Returns("bucket");
        bucket.Collections.Returns(collectionManager);
        collectionManager
            .GetAllScopesAsync(Arg.Any<GetAllScopesOptions?>())
            .Returns(
                Task.FromResult<IEnumerable<ScopeSpec>>([new("scope") { Collections = [new("scope", "existing")] }])
            );

        var specs = await _GetBucketScopeSpecsAsync(manager, "cluster", bucket);

        specs.Should().BeAssignableTo<FrozenDictionary<string, FrozenSet<string>>>();
        specs["scope"].Should().BeAssignableTo<FrozenSet<string>>();
        specs["scope"].Should().Contain("existing");
    }

    private static async Task<FrozenDictionary<string, FrozenSet<string>>> _GetBucketScopeSpecsAsync(
        CouchbaseManager manager,
        string clusterKey,
        IBucket bucket
    )
    {
        var method = typeof(CouchbaseManager).GetMethod(
            "_GetBucketScopeSpecsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null,
            [typeof(string), typeof(IBucket)],
            modifiers: null
        );

        method.Should().NotBeNull();

        var task = method!.Invoke(manager, [clusterKey, bucket]);

        task.Should().BeOfType<Task<FrozenDictionary<string, FrozenSet<string>>>>();

        return await ((Task<FrozenDictionary<string, FrozenSet<string>>>)task!).ConfigureAwait(false);
    }
}
