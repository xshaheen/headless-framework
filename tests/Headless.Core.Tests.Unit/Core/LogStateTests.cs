// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;
using Microsoft.Extensions.Logging;

namespace Tests.Core;

public sealed class LogStateTests
{
    [Fact]
    public void should_accumulate_tags_across_multiple_calls_when_tag()
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
    public void should_not_throw_when_critical_then_tag()
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
    public void should_deduplicate_case_insensitively_when_tag()
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
    public void should_overwrite_an_existing_key_when_property()
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
    public void should_not_throw_on_duplicate_keys_when_properties()
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
    public void should_throw_on_a_null_key_when_property()
    {
        // given
        var state = new LogState();

        // when
        Action act = () => state.Property(null!, "v");

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_on_a_null_key_when_property_if()
    {
        // given
        var state = new LogState();

        // when
        Action act = () => state.PropertyIf(null!, "v", condition: true);

        // then
        act.Should().Throw<ArgumentException>();
    }
}
