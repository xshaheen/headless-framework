// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests.RegularLocks;

public sealed class WaiterCapRegistryTests : TestBase
{
    [Fact]
    public void should_free_the_slot_when_a_waiter_exits()
    {
        // Cap of one waiter per resource; Enter then Exit must return the slot so a fresh Enter succeeds.
        var registry = new WaiterCapRegistry(maxConcurrentWaitingResources: null, maxWaitersPerResource: 1);

        registry.Enter("resource");
        registry.Exit("resource");

        var reenter = () => registry.Enter("resource");
        reenter.Should().NotThrow(); // slot was freed by Exit

        // The single slot is now taken again, so a second concurrent waiter is rejected.
        var overflow = () => registry.Enter("resource");
        overflow.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_when_max_waiters_per_resource_is_exceeded()
    {
        var registry = new WaiterCapRegistry(maxConcurrentWaitingResources: null, maxWaitersPerResource: 2);

        registry.Enter("resource");
        registry.Enter("resource");

        var act = () => registry.Enter("resource");

        act.Should().Throw<InvalidOperationException>().WithMessage("*waiters per resource*");
    }

    [Fact]
    public void should_throw_when_max_concurrent_waiting_resources_is_exceeded()
    {
        var registry = new WaiterCapRegistry(maxConcurrentWaitingResources: 2, maxWaitersPerResource: null);

        registry.Enter("a");
        registry.Enter("b");

        var act = () => registry.Enter("c"); // third distinct contended resource

        act.Should().Throw<InvalidOperationException>().WithMessage("*concurrent waiting resources*");
    }

    [Fact]
    public void should_not_count_additional_waiters_on_an_existing_resource_against_the_resource_cap()
    {
        // The aggregate cap counts distinct contended resources, not total waiters.
        var registry = new WaiterCapRegistry(maxConcurrentWaitingResources: 1, maxWaitersPerResource: null);

        registry.Enter("a");

        var act = () => registry.Enter("a"); // same resource, not a new distinct resource

        act.Should().NotThrow();
    }

    [Fact]
    public void should_never_throw_when_both_caps_are_null()
    {
        var registry = new WaiterCapRegistry(maxConcurrentWaitingResources: null, maxWaitersPerResource: null);

        var act = () =>
        {
            for (var resource = 0; resource < 50; resource++)
            {
                for (var waiter = 0; waiter < 50; waiter++)
                {
                    registry.Enter($"resource-{resource}");
                }
            }
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void should_treat_an_unmatched_exit_as_a_no_op()
    {
        var registry = new WaiterCapRegistry(maxConcurrentWaitingResources: null, maxWaitersPerResource: 1);

        var exit = () => registry.Exit("never-entered");
        exit.Should().NotThrow();

        // State must be unchanged: the cap still admits exactly one waiter, no underflow into a negative slot.
        registry.Enter("never-entered");

        var overflow = () => registry.Enter("never-entered");
        overflow.Should().Throw<InvalidOperationException>();
    }
}
