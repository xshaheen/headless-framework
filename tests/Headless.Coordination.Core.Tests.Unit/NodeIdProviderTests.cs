// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Coordination;
using Headless.Testing.Tests;

namespace Tests;

public sealed class NodeIdProviderTests : TestBase
{
    [Fact]
    public async Task should_prefer_configured_node_id()
    {
        // given
        var provider = _CreateProvider(new CoordinationOptions { ConfiguredNodeId = "configured" }, "host-a");

        // when
        var nodeId = await provider.GetNodeIdAsync(AbortToken);

        // then
        nodeId.Should().Be(new NodeId("configured"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_use_discovered_host_name_when_configured_id_is_absent(string? configured)
    {
        // given
        var provider = _CreateProvider(new CoordinationOptions { ConfiguredNodeId = configured }, "prod/orders-7d");

        // when
        var nodeId = await provider.GetNodeIdAsync(AbortToken);

        // then the membership store sees the same host that stamps messages and logs
        nodeId.Should().Be(new NodeId("prod/orders-7d"));
    }

    private static DefaultNodeIdProvider _CreateProvider(CoordinationOptions options, string hostName)
    {
        var hostIdentity = Substitute.For<IHostIdentityAccessor>();
        hostIdentity.HostName.Returns(hostName);

        return new DefaultNodeIdProvider(options, hostIdentity);
    }
}
