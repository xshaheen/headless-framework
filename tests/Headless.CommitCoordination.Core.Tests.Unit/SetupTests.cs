// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests
{
    [Fact]
    public void should_not_stack_duplicate_current_coordinator_descriptors_when_add_commit_coordination()
    {
        // The ICurrentCommitCoordinator registration is deliberately unconditional (not TryAdd) so it wins
        // last over messaging's null-coordinator fallback. The sentinel keeps repeated calls from stacking
        // duplicate descriptors while preserving that last-wins ordering.
        var services = new ServiceCollection();

        services.AddCommitCoordination();
        services.AddCommitCoordination();

        services.Count(d => d.ServiceType == typeof(ICurrentCommitCoordinator)).Should().Be(1);
    }

    [Fact]
    public void should_resolve_the_real_scope_stack_as_current_coordinator_when_add_commit_coordination()
    {
        var services = new ServiceCollection();
        services.AddCommitCoordination();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ICurrentCommitCoordinator>().Should().BeOfType<CommitScopeStack>();
    }
}
