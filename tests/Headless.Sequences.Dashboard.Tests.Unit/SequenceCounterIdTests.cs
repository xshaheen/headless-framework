// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SequenceCounterIdTests : TestBase
{
    [Theory]
    [InlineData("acme", "invoice", "2026", "acme/invoice/2026")]
    [InlineData("", "ticket", "", "/ticket/")]
    [InlineData("a/b", "100%", "x y", "a%2Fb/100%25/x%20y")]
    public void should_format_a_key_as_its_escaped_parts_and_read_it_back(
        string tenant,
        string name,
        string partition,
        string expected
    )
    {
        // given
        var key = new SequenceKey(tenant, name, partition);

        // when
        var id = SequenceCounterId.Format(key);

        // then
        id.Should().Be(expected);
        SequenceCounterId.TryParse(id, out var parsed).Should().BeTrue();
        parsed.Should().Be(key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("acme/invoice")]
    [InlineData("acme//2026")]
    [InlineData("a/b/c/d")]
    [InlineData("acme/in%2voice/2026")]
    [InlineData("acme/in voice/2026")]
    [InlineData("acme/invoice/%202026")]
    [InlineData("acme/invoice/%20")]
    public void should_refuse_text_that_is_not_an_id_this_module_served(string? id)
    {
        SequenceCounterId.TryParse(id, out _).Should().BeFalse();
    }

    [Fact]
    public void should_read_back_both_cursors_and_refuse_a_foreign_one()
    {
        // given
        var key = new SequenceKey("acme", "invoice/a", "2026");

        // when
        var list = SequenceCounterId.FormatListCursor(key);
        var partitions = SequenceCounterId.FormatPartitionCursor("2025");

        // then
        SequenceCounterId.TryParseListCursor(list, out var after).Should().BeTrue();
        after.Should().Be(key);
        SequenceCounterId.TryParsePartitionCursor(partitions, out var before).Should().BeTrue();
        before.Should().Be("2025");
        SequenceCounterId.TryParseListCursor(partitions, out _).Should().BeFalse();
        SequenceCounterId.TryParsePartitionCursor(list, out _).Should().BeFalse();
        SequenceCounterId.TryParseListCursor("c1.!!", out _).Should().BeFalse();
        SequenceCounterId.TryParsePartitionCursor("p1.", out _).Should().BeFalse();
    }
}
