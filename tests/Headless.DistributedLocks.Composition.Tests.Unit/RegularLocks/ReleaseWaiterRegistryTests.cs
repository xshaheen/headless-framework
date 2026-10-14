// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests.RegularLocks;

public sealed class ReleaseWaiterRegistryTests : TestBase
{
    [Fact]
    public async Task should_complete_wait_when_resource_is_signalled()
    {
        // given
        var sut = new ReleaseWaiterRegistry();
        var entry = sut.Enter("resource");

        // when
        sut.Signal("resource");

        // then
        await entry.WaitAsync(AbortToken).Bounded();
        sut.Exit("resource", entry);
    }

    [Fact]
    public void should_drop_entry_when_last_waiter_exits()
    {
        // given - a waiter enters and leaves, then a release arrives with nobody waiting
        var sut = new ReleaseWaiterRegistry();
        var first = sut.Enter("resource");
        sut.Exit("resource", first);
        sut.Signal("resource");

        // when
        var next = sut.Enter("resource");

        // then - the next waiter gets a fresh entry; a retained entry would carry the stale signal over
        next.Should().NotBeSameAs(first);
        next.Event.IsSet.Should().BeFalse();
        sut.Exit("resource", next);
    }

    [Fact]
    public void should_keep_entry_while_another_waiter_remains()
    {
        // given - two waiters share the resource's entry
        var sut = new ReleaseWaiterRegistry();
        var first = sut.Enter("resource");
        var second = sut.Enter("resource");

        // when - one leaves and a release arrives
        sut.Exit("resource", first);
        sut.Signal("resource");

        // then - the remaining waiter still receives it
        second.Should().BeSameAs(first);
        second.Event.IsSet.Should().BeTrue();
        sut.Exit("resource", second);
    }
}
