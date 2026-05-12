// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests;

public sealed class OrderExtensionsTests
{
    private sealed record Item(string Name, int Priority, int Month);

    private readonly List<Item> _items =
    [
        new("alpha", 2, 01),
        new("beta", 1, 03),
        new("beta", 2, 02),
        new("alpha", 1, 04),
    ];

    [Fact]
    public void should_order_by_single_property_when_one_order_supplied()
    {
        // given
        var orders = new[] { new OrderBy("Name", Ascending: true) };

        // when
        var result = _items.AsQueryable().OrderBy(orders).Select(x => x.Name).ToList();

        // then
        result.Should().Equal("alpha", "alpha", "beta", "beta");
    }

    [Fact]
    public void should_apply_all_orders_in_sequence_when_two_orders_supplied()
    {
        // given — primary Name asc, secondary Priority asc
        var orders = new[] { new OrderBy("Name", Ascending: true), new OrderBy("Priority", Ascending: true) };

        // when
        var result = _items.AsQueryable().OrderBy(orders).Select(x => $"{x.Name}/{x.Priority}").ToList();

        // then — without the secondary, alpha/1 and alpha/2 could appear in any order
        result.Should().Equal("alpha/1", "alpha/2", "beta/1", "beta/2");
    }

    [Fact]
    public void should_apply_three_level_ordering_when_three_orders_supplied()
    {
        // given — Name asc, Priority desc, Month asc
        var orders = new[]
        {
            new OrderBy("Name", Ascending: true),
            new OrderBy("Priority", Ascending: false),
            new OrderBy("Month", Ascending: true),
        };

        // when
        var result = _items.AsQueryable().OrderBy(orders).Select(x => $"{x.Name}/{x.Priority}/{x.Month}").ToList();

        // then — alpha grouped first (asc), inside Priority desc, no ties on Month so the third key
        // does not change order but its presence must not throw or discard the prior two
        result.Should().Equal("alpha/2/1", "alpha/1/4", "beta/2/2", "beta/1/3");
    }
}
