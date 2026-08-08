// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Couchbase;
using Headless.Couchbase.Clusters;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Nito.AsyncEx;

namespace Tests;

public sealed class CouchbaseClustersProviderTests : TestBase
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task should_reject_missing_cluster_key(string? clusterKey)
    {
        await using var sut = _CreateProvider(
            Substitute.For<ICouchbaseClusterOptionsProvider>(),
            Substitute.For<ICouchbaseTransactionConfigProvider>()
        );

        var act = () => sut.GetClusterAsync(clusterKey!, AbortToken).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_share_connection_attempt_when_one_caller_cancels_its_wait()
    {
        var releaseConnection = new TaskCompletionSource<CouchbaseClusterConnection>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var optionsProvider = Substitute.For<ICouchbaseClusterOptionsProvider>();
        var sut = _CreateProvider(optionsProvider, Substitute.For<ICouchbaseTransactionConfigProvider>());
        var lazy = new AsyncLazy<CouchbaseClusterConnection>(() => releaseConnection.Task);
        _GetCache(sut).TryAdd("primary", lazy).Should().BeTrue();
        var cancellation = new CancellationTokenSource();

        try
        {
            var abandonedWait = sut.GetClusterAsync("primary", cancellation.Token).AsTask();
            var sharedWait = sut.GetClusterAsync("primary", AbortToken).AsTask();
            await cancellation.CancelAsync();
            var cluster = CouchbaseTestFactory.CreateCluster();
            var connection = new CouchbaseClusterConnection
            {
                Cluster = cluster,
                Transactions = CouchbaseTestFactory.CreateTransactions(cluster),
            };
            releaseConnection.SetResult(connection);
            Func<Task> abandonedAct = async () => _ = await abandonedWait;

            await abandonedAct.Should().ThrowAsync<OperationCanceledException>();
            var resolved = await sharedWait.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            resolved.Should().BeSameAs(connection);
            await optionsProvider.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
        }
        finally
        {
            releaseConnection.TrySetCanceled();
            cancellation.Dispose();
            await sut.DisposeAsync();
        }
    }

    [Fact]
    public async Task should_evict_failed_connection_so_next_call_retries()
    {
        var optionsProvider = Substitute.For<ICouchbaseClusterOptionsProvider>();
        var failure = Task.FromException<ClusterOptions>(new InvalidOperationException("options unavailable"));
        optionsProvider.GetAsync("primary", CancellationToken.None).Returns(new ValueTask<ClusterOptions>(failure));
        await using var sut = _CreateProvider(optionsProvider, Substitute.For<ICouchbaseTransactionConfigProvider>());

        var first = () => sut.GetClusterAsync("primary", AbortToken).AsTask();
        var second = () => sut.GetClusterAsync("primary", AbortToken).AsTask();

        await first.Should().ThrowAsync<InvalidOperationException>().WithMessage("options unavailable");
        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("options unavailable");
        await optionsProvider.Received(2).GetAsync("primary", CancellationToken.None);
    }

    [Fact]
    public async Task should_dispose_completed_cluster_connection()
    {
        var cluster = CouchbaseTestFactory.CreateCluster();
        var transactions = CouchbaseTestFactory.CreateTransactions(cluster);
        var connection = new CouchbaseClusterConnection { Cluster = cluster, Transactions = transactions };
        var lazy = new AsyncLazy<CouchbaseClusterConnection>(() => Task.FromResult(connection));
        _ = await lazy.Task;
        var sut = _CreateProvider(
            Substitute.For<ICouchbaseClusterOptionsProvider>(),
            Substitute.For<ICouchbaseTransactionConfigProvider>()
        );
        _GetCache(sut).TryAdd("primary", lazy).Should().BeTrue();

        await sut.DisposeAsync();

        await cluster.Received(1).DisposeAsync();
    }

    private static CouchbaseClustersProvider _CreateProvider(
        ICouchbaseClusterOptionsProvider optionsProvider,
        ICouchbaseTransactionConfigProvider transactionProvider
    )
    {
        return new(optionsProvider, transactionProvider, NullLogger<CouchbaseClustersProvider>.Instance);
    }

    private static ConcurrentDictionary<string, AsyncLazy<CouchbaseClusterConnection>> _GetCache(
        CouchbaseClustersProvider provider
    )
    {
        var cacheField = typeof(CouchbaseClustersProvider).GetField(
            "_clusters",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
        );
        var cache =
            cacheField?.GetValue(provider) as ConcurrentDictionary<string, AsyncLazy<CouchbaseClusterConnection>>;
        cache.Should().NotBeNull();

        return cache!;
    }
}
