// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SequenceRangeTests : TestBase
{
    [Fact]
    public void should_enumerate_each_value_a_step_apart_and_end_at_last()
    {
        // given
        var range = new SequenceRange(10, 3, 5);

        // when
        var values = new List<long>();
        foreach (var value in range)
        {
            values.Add(value);
        }

        // then
        values.Should().Equal(10, 15, 20);
        range.Last.Should().Be(20);
    }

    [Fact]
    public void should_enumerate_exactly_first_when_count_is_one()
    {
        // given
        var range = new SequenceRange(7, 1, 3);

        // when / then
        range.Should().Equal(7L);
        range.Last.Should().Be(7);
    }

    [Fact]
    public void should_support_linq_through_the_interface()
    {
        new SequenceRange(1, 4, 1).Sum().Should().Be(10);
    }

    [Fact]
    public void should_restart_after_reset()
    {
        // given
        var enumerator = new SequenceRange(1, 2, 1).GetEnumerator();
        enumerator.MoveNext();
        enumerator.MoveNext();

        // when
        enumerator.Reset();

        // then
        enumerator.MoveNext().Should().BeTrue();
        enumerator.Current.Should().Be(1);
    }

    [Fact]
    public void should_compare_ranges_by_value()
    {
        (new SequenceRange(1, 3, 2) == new SequenceRange(1, 3, 2)).Should().BeTrue();
        (new SequenceRange(1, 3, 2) == new SequenceRange(1, 3, 1)).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    public void should_refuse_a_count_or_step_that_is_not_positive(int count, long step)
    {
        var act = () => new SequenceRange(1, count, step);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_refuse_a_range_whose_last_value_overflows()
    {
        var act = () => new SequenceRange(long.MaxValue - 1, 3, 1);

        act.Should().Throw<OverflowException>();
    }
}
