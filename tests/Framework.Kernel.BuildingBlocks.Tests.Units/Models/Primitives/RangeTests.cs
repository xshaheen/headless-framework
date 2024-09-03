using Framework.Kernel.Primitives;

namespace Tests.Models.Primitives;

public class RangeTests
{
    #region Creation

    [Fact]
    public void ctor_should_create_date_range_when_provide_inorder_from_and_to()
    {
        // given
        var from = new DateOnly(2021, 1, 1);
        var to = new DateOnly(2021, 1, 31);

        // when
        var range = new Range<DateOnly>(from, to);

        // then
        range.From.Should().Be(from);
        range.To.Should().Be(to);
    }

    [Fact]
    public void ctor_should_throw_exception_when_provide_inorder_from_and_to()
    {
        // given
        const int from = 31;
        const int to = 1;

        // when
        var action = FluentActions.Invoking(() => new Range<int>(from, to));

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region Value Inclusive Has

    public static readonly TheoryData<Range<int>, int, bool> ValueInclusiveHasData =
        new()
        {
            { new(1, 10), 5, true }, // Middle
            { new(1, 10), 1, true }, // From Edge
            { new(1, 10), 10, true }, // To Edge
            { new(1, 10), 0, false }, // Below
            { new(1, 10), 11, false }, // Above
        };

    [Theory]
    [MemberData(nameof(ValueInclusiveHasData))]
    public void value_inclusive_has_should_return_true_when_value_is_in_range(
        Range<int> range,
        int value,
        bool expected
    )
    {
        // when
        var result = range.InclusiveHas(value);

        // then
        result.Should().Be(expected);
    }

    #endregion

    #region Exclusive Has

    public static readonly TheoryData<Range<int>, int, bool> ValueExclusiveHasData =
        new()
        {
            { new(1, 10), 5, true }, // Middle
            { new(1, 10), 1, false }, // From Edge
            { new(1, 10), 10, false }, // To Edge
            { new(1, 10), 0, false }, // Below
            { new(1, 10), 11, false }, // Above
        };

    [Theory]
    [MemberData(nameof(ValueExclusiveHasData))]
    public void value_exclusive_has_should_return_true_when_value_is_in_range(
        Range<int> range,
        int value,
        bool expected
    )
    {
        // when
        var result = range.ExclusiveHas(value);

        // then
        result.Should().Be(expected);
    }

    #endregion

    #region Is Overlap

    public static readonly TheoryData<Range<int>, Range<int>, bool> IsOverlapData =
        new()
        {
            { new(1, 10), new(5, 15), true }, // Middle
            { new(1, 10), new(1, 10), true }, // Same
            { new(1, 10), new(1, 5), true }, // From Edge
            { new(1, 10), new(5, 10), true }, // To Edge
            { new(1, 10), new(0, 5), true }, // Below
            { new(1, 10), new(5, 11), true }, // Above
            { new(1, 10), new(11, 15), false }, // After
            { new(1, 10), new(15, 20), false }, // Far After
        };

    [Theory]
    [MemberData(nameof(IsOverlapData))]
    public void is_overlap_should_return_true_when_ranges_overlap(Range<int> range, Range<int> other, bool expected)
    {
        // when
        var result = range.IsOverlap(other);

        // then
        result.Should().Be(expected);
    }

    #endregion

    #region RemoveConflictRangeParts

    public static readonly TheoryData<Range<int>, Range<int>, Range<int>[]> RemoveOverlapData =
        new()
        {
            // Same range
            { new(1, 10), new(1, 10), [] },
            { new(1, 10), new(1, 5), [new(6, 10)] }, // From Edge
            { new(1, 10), new(5, 10), [new(1, 4)] }, // To Edge
            { new(1, 10), new(11, 15), [new(1, 10)] }, // After
            { new(1, 10), new(15, 20), [new(1, 10)] }, // Far After
            { new(1, 10), new(5, 15), [new(1, 4)] }, // Middle
            { new(1, 10), new(0, 5), [new(6, 10)] }, // Below
            { new(1, 10), new(5, 11), [new(1, 4)] }, // Above
        };

    [Theory]
    [MemberData(nameof(RemoveOverlapData))]
    public void remove_overlap_should_return_ranges_without_overlap(
        Range<int> range,
        Range<int> other,
        Range<int>[] remaining
    )
    {
        // when
        var result = range.RemoveConflictRangeParts(other, x => x + 1, x => x - 1);

        // then
        result.Should().BeEquivalentTo(remaining);
    }

    #endregion
}
