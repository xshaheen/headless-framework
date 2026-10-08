// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Redis;
using Headless.Testing.Tests;

namespace Tests;

public sealed class RedisConsumerNamesTests : TestBase
{
    [Fact]
    public void should_give_live_clients_of_one_group_distinct_slots_and_other_groups_their_own()
    {
        // given
        var names = new RedisConsumerNames("host-a");

        // when
        var first = names.Acquire("orders");
        var second = names.Acquire("orders");
        var other = names.Acquire("billing");

        // then
        first.Name.Should().Be("orders:host-a:0");
        second.Name.Should().Be("orders:host-a:1");
        other.Name.Should().Be("billing:host-a:0");
    }

    [Fact]
    public void should_reuse_the_lowest_released_slot_so_a_replacement_resumes_its_predecessors_name()
    {
        // given
        var names = new RedisConsumerNames("host-a");
        var first = names.Acquire("orders");
        _ = names.Acquire("orders");

        // when
        first.Release();
        first.Release();
        var replacement = names.Acquire("orders");
        var next = names.Acquire("orders");

        // then
        replacement.Name.Should().Be("orders:host-a:0");
        next.Name.Should().Be("orders:host-a:2");
    }
}
