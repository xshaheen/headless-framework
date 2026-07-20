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

    [Fact]
    public void should_enforce_caps_when_both_limits_are_configured()
    {
        // given
        var registry = new WaiterCapRegistry(maxConcurrentWaitingResources: 3, maxWaitersPerResource: 2);

        // Fill resource "r1" to its cap (2 waiters)
        registry.Enter("r1");
        registry.Enter("r1");

        // Next waiter on "r1" should be rejected
        var actR1Overflow = () => registry.Enter("r1");
        actR1Overflow.Should().Throw<InvalidOperationException>().WithMessage("*waiters per resource*");

        // Fill distinct resources to the max resource cap (3 resources: r1, r2, r3)
        registry.Enter("r2");
        registry.Enter("r3");

        // Next distinct resource "r4" should be rejected
        var actR4Overflow = () => registry.Enter("r4");
        actR4Overflow.Should().Throw<InvalidOperationException>().WithMessage("*concurrent waiting resources*");

        // Leaving waiters frees capacity
        registry.Exit("r1"); // r1 now has 1 waiter
        registry.Exit("r1"); // r1 now has 0 waiters (removed)

        // Now we only have r2 and r3 waiting (2 resources), so r4 can enter
        var actR4Enter = () => registry.Enter("r4");
        actR4Enter.Should().NotThrow();

        // Exit r2 so we have r3 and r4 waiting (2 resources)
        registry.Exit("r2");

        // And since we now have capacity, we can enter r1 again
        var actR1Enter = () => registry.Enter("r1");
        actR1Enter.Should().NotThrow();
    }
}
