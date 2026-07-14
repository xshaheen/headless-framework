// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;
using Microsoft.Extensions.Logging;

namespace Tests.Core;

public sealed class LogStateTests
{
    [Fact]
    public void tag_should_accumulate_tags_across_multiple_calls()
    {
        // given
        var state = new LogState();

        // when
        Action act = () => state.Tag("orders").Tag("billing");

        // then
        act.Should().NotThrow();
        var tags = (List<string>)state["Tags"]!;
        tags.Should().BeEquivalentTo(["orders", "billing"]);
    }

    [Fact]
    public void critical_then_tag_should_not_throw()
    {
        // given
        var state = new LogState();

        // when
        Action act = () => state.Critical().Tag("orders");

        // then
        act.Should().NotThrow();
        var tags = (List<string>)state["Tags"]!;
        tags.Should().Contain(["Critical", "orders"]);
    }

    [Fact]
    public void tag_should_deduplicate_case_insensitively()
    {
        // given
        var state = new LogState();

        // when
        state.Tag("Orders").Tag("orders");

        // then
        var tags = (List<string>)state["Tags"]!;
        tags.Should().ContainSingle();
    }

    [Fact]
    public void property_should_overwrite_an_existing_key()
    {
        // given
        var state = new LogState();

        // when
        Action act = () => state.Property("orderId", 1).Property("orderId", 2);

        // then
        act.Should().NotThrow();
        state["orderId"].Should().Be(2);
        state.Count.Should().Be(1);
    }

    [Fact]
    public void properties_should_not_throw_on_duplicate_keys()
    {
        // given
        var state = new LogState();
        var pairs = new List<KeyValuePair<string?, string?>> { new("k", "1"), new("k", "2") };

        // when
        Action act = () => state.Properties(pairs);

        // then
        act.Should().NotThrow();
        state["k"].Should().Be("2");
    }

    [Fact]
    public void property_should_throw_on_a_null_key()
    {
        // given
        var state = new LogState();

        // when
        Action act = () => state.Property(null!, "v");

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void property_if_should_throw_on_a_null_key()
    {
        // given
        var state = new LogState();

        // when
        Action act = () => state.PropertyIf(null!, "v", condition: true);

        // then
        act.Should().Throw<ArgumentException>();
    }
}
