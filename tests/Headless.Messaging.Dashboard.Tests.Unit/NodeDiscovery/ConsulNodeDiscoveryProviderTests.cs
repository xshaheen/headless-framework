// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Testing.Tests;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.NodeDiscovery;

public sealed class ConsulNodeDiscoveryProviderTests : TestBase
{
    [Fact]
    public async Task should_preserve_caller_cancellation_when_getting_node()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        var provider = _CreateProvider(cache);

        Func<Task> act = async () =>
            _ = await provider.GetNodeAsync("test-node", cancellationToken: cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_preserve_caller_cancellation_when_getting_nodes()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        var provider = _CreateProvider(cache);

        Func<Task> act = async () => _ = await provider.GetNodesAsync(cancellationToken: cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_preserve_caller_cancellation_when_registering_node()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        var provider = _CreateProvider(cache);

        var act = () => provider.RegisterNodeAsync(cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ConsulNodeDiscoveryProvider _CreateProvider(IMemoryCache cache)
    {
        return new(
            NullLoggerFactory.Instance,
            cache,
            new ConsulDiscoveryOptions
            {
                DiscoveryServerHostName = "127.0.0.1",
                DiscoveryServerPort = 1,
                NodeName = "test-node",
            }
        );
    }
}
