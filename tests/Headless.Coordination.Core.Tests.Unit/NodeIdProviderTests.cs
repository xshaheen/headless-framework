// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Coordination;
using Headless.Testing.Tests;

namespace Tests;

public sealed class NodeIdProviderTests : TestBase
{
    [Fact]
    public async Task should_use_the_host_name_the_framework_resolved()
    {
        // given
        var hostIdentity = Substitute.For<IHostIdentityAccessor>();
        hostIdentity.HostName.Returns("prod/orders-7d");
        var provider = new DefaultNodeIdProvider(hostIdentity);

        // when
        var nodeId = await provider.GetNodeIdAsync(AbortToken);

        // then the membership store sees the same host that stamps messages and logs
        nodeId.Should().Be(new NodeId("prod/orders-7d"));
    }
}
