// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerMembershipFixture>]
public sealed class SqlServerMembershipNativeTests(SqlServerMembershipFixture fixture)
{
    [Fact]
    public async Task should_build_membership_provider()
    {
        await using var node = await fixture.CreateNodeAsync("native-" + Guid.NewGuid().ToString("N"), "node-a");

        node.Membership.Should().NotBeNull();
    }
}
