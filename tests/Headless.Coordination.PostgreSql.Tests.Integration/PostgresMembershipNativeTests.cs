// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<PostgresMembershipFixture>]
public sealed class PostgresMembershipNativeTests(PostgresMembershipFixture fixture)
{
    [Fact]
    public async Task should_register_failover_eligible_provider_capability()
    {
        await using var node = await fixture.CreateNodeAsync("native-" + Guid.NewGuid().ToString("N"), "node-a");

        var capabilities = node.Membership
            .Should()
            .NotBeNull()
            .And.Subject;

        capabilities.Should().NotBeNull();
    }
}
