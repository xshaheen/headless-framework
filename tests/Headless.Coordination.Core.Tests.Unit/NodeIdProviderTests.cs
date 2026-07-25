// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class NodeIdProviderTests : TestBase
{
    [Fact]
    public async Task should_prefer_configured_node_id()
    {
        // given
        var provider = _CreateProvider(new CoordinationOptions { ConfiguredNodeId = "configured" });

        // when
        var nodeId = await provider.GetNodeIdAsync(AbortToken);

        // then
        nodeId.Should().Be(new NodeId("configured"));
    }

    [Fact]
    public async Task should_use_pod_name_and_namespace_when_configured_id_is_absent()
    {
        // given
        var provider = _CreateProvider(env: name =>
            name switch
            {
                "POD_NAME" => "orders-7d",
                "POD_NAMESPACE" => "prod",
                _ => null,
            }
        );

        // when
        var nodeId = await provider.GetNodeIdAsync(AbortToken);

        // then
        nodeId.Should().Be(new NodeId("prod/orders-7d"));
    }

    [Fact]
    public async Task should_use_pod_name_without_namespace_when_namespace_is_absent()
    {
        // given
        var provider = _CreateProvider(env: name =>
            name switch
            {
                "POD_NAME" => "orders-7d",
                _ => null,
            }
        );

        // when
        var nodeId = await provider.GetNodeIdAsync(AbortToken);

        // then
        nodeId.Should().Be(new NodeId("orders-7d"));
    }

    [Fact]
    public async Task should_use_hostname_when_pod_name_is_absent()
    {
        // given
        var provider = _CreateProvider(hostName: () => "host-a");

        // when
        var nodeId = await provider.GetNodeIdAsync(AbortToken);

        // then
        nodeId.Should().Be(new NodeId("host-a"));
    }

    [Fact]
    public async Task should_generate_fallback_node_id_when_no_stable_source_exists()
    {
        // given
        var guid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var provider = _CreateProvider(hostName: () => "", guidGenerator: new FixedGuidGenerator(guid));

        // when
        var nodeId = await provider.GetNodeIdAsync(AbortToken);

        // then
        nodeId.Should().Be(new NodeId("generated-11111111111111111111111111111111"));
    }

    private static DefaultNodeIdProvider _CreateProvider(
        CoordinationOptions? options = null,
        Func<string, string?>? env = null,
        Func<string>? hostName = null,
        IGuidGenerator? guidGenerator = null
    )
    {
        return new DefaultNodeIdProvider(
            options ?? new CoordinationOptions(),
            guidGenerator ?? new FixedGuidGenerator(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            NullLogger<DefaultNodeIdProvider>.Instance,
            env ?? (_ => null),
            hostName ?? (() => "")
        );
    }

    private sealed class FixedGuidGenerator(Guid guid) : IGuidGenerator
    {
        public Guid Create()
        {
            return guid;
        }
    }
}
