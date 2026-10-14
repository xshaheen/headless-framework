// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.Consul;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ConsulProcessingNodeServerTests : TestBase
{
    [Fact]
    public async Task should_register_current_node_with_stopping_token_when_started()
    {
        // given
        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        await using var server = new ConsulProcessingNodeServer(discoveryProvider);

        // when
        await server.StartAsync(AbortToken);

        // then - peers find this node only after it registers itself with Consul at startup
        await discoveryProvider.Received(1).RegisterNodeAsync(AbortToken);
    }
}
