// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection(nameof(RedisMembershipFixture))]
public sealed class RedisMembershipLuaTests(RedisMembershipFixture fixture)
{
    [Fact]
    public async Task should_build_membership_provider()
    {
        await using var node = await fixture.CreateNodeAsync("native-" + Guid.NewGuid().ToString("N"), "node-a");

        node.Membership.Should().NotBeNull();
    }
}
