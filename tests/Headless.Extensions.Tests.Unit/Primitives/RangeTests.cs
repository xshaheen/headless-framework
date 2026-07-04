// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class RangeTests
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

    public static readonly TheoryData<Range<int>, int, bool> ValueInclusiveHasData = new()
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

    #region Range Inclusive Has

    public static readonly TheoryData<Range<string>, Range<string>, bool> RangeInclusiveHasData = new()
    {
        { new Range<string>("a", null), new Range<string>("m", null), true },
        { new Range<string>("a", "z"), new Range<string>("m", null), false },
        { new Range<string>(null, "z"), new Range<string>(null, "m"), true },
        { new Range<string>("a", "z"), new Range<string>(null, "m"), false },
        { new Range<string>(null, "z"), new Range<string>("a", "z"), true },
        { new Range<string>("a", null), new Range<string>("a", "z"), true },
    };

    [Theory]
    [MemberData(nameof(RangeInclusiveHasData))]
    public void range_inclusive_has_should_handle_unbounded_bounds(
        Range<string> range,
        Range<string> other,
        bool expected
    )
    {
        // when
        var result = range.InclusiveHas(other);

        // then
        result.Should().Be(expected);
    }

    #endregion

    #region Exclusive Has

    public static readonly TheoryData<Range<int>, int, bool> ValueExclusiveHasData = new()
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

    public static TheoryData<Range<int>, Range<int>, bool> IsOverlapData =>
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

    public static readonly TheoryData<Range<string>, Range<string>, bool> IsOverlapUnboundedData = new()
    {
        { new Range<string>("m", null), new Range<string>(null, "l"), false },
        { new Range<string>("m", null), new Range<string>(null, "m"), true },
        { new Range<string>(null, "m"), new Range<string>("n", null), false },
        { new Range<string>(null, "m"), new Range<string>("m", null), true },
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

    [Theory]
    [MemberData(nameof(IsOverlapUnboundedData))]
    public void is_overlap_should_handle_unbounded_bounds(Range<string> range, Range<string> other, bool expected)
    {
        // when
        var result = range.IsOverlap(other);

        // then
        result.Should().Be(expected);
    }

    #endregion

    #region RemoveConflictRangeParts

    public static readonly TheoryData<Range<int>, Range<int>, Range<int>[]> RemoveOverlapData = new()
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

    public static readonly TheoryData<Range<string>, Range<string>, Range<string>[]> RemoveUnboundedOverlapData = new()
    {
        {
            new Range<string>(null, "z"),
            new Range<string>("m", "p"),
            [new Range<string>(null, "l"), new Range<string>("q", "z")]
        },
        {
            new Range<string>("a", null),
            new Range<string>("m", "p"),
            [new Range<string>("a", "l"), new Range<string>("q", null)]
        },
        { new Range<string>(null, "z"), new Range<string>(null, "m"), [new Range<string>("n", "z")] },
        { new Range<string>("a", null), new Range<string>("m", null), [new Range<string>("a", "l")] },
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

    [Theory]
    [MemberData(nameof(RemoveUnboundedOverlapData))]
    public void remove_overlap_should_preserve_unbounded_remainders(
        Range<string> range,
        Range<string> other,
        Range<string>[] remaining
    )
    {
        // when
        var result = range.RemoveConflictRangeParts(other, _NextLetter, _PreviousLetter);

        // then
        result.Should().BeEquivalentTo(remaining);
    }

    private static string _NextLetter(string value) => ((char)(value[0] + 1)).ToString();

    private static string _PreviousLetter(string value) => ((char)(value[0] - 1)).ToString();

    #endregion

    #region CompareTo

    [Fact]
    public void compare_to_should_sort_unbounded_lower_bound_before_any_value()
    {
        // given - (-inf, "5"] vs ["3", "5"]; an unbounded lower bound must sort first.
        // Range<T> bounds are T? with T : IComparable<T> and no struct/class constraint, so a null bound is only
        // constructible for a reference type — use Range<string> (single-char values keep ordinal order intuitive).
        var unboundedBelow = new Range<string>(null, "5");
        var bounded = new Range<string>("3", "5");

        // then
        unboundedBelow.CompareTo(bounded).Should().BeNegative();
        bounded.CompareTo(unboundedBelow).Should().BePositive();
    }

    [Fact]
    public void compare_to_should_sort_unbounded_upper_bound_after_any_value()
    {
        // given - ["3", +inf) vs ["3", "5"]; an unbounded upper bound must sort last
        var unboundedAbove = new Range<string>("3", null);
        var bounded = new Range<string>("3", "5");

        // then
        unboundedAbove.CompareTo(bounded).Should().BePositive();
        bounded.CompareTo(unboundedAbove).Should().BeNegative();
    }

    [Fact]
    public void compare_to_should_be_consistent_with_equals_for_unbounded_ranges()
    {
        // given - the bug: (-inf, "5"] used to compare EQUAL to ["3", "5"] while Equals reported them different
        var unboundedBelow = new Range<string>(null, "5");
        var bounded = new Range<string>("3", "5");

        // then - CompareTo == 0 must agree with Equals
        unboundedBelow.Equals(bounded).Should().BeFalse();
        unboundedBelow.CompareTo(bounded).Should().NotBe(0);

        // and equal-bounds ranges agree the other way
        var sameUnbounded = new Range<string>(null, "5");
        unboundedBelow.Equals(sameUnbounded).Should().BeTrue();
        unboundedBelow.CompareTo(sameUnbounded).Should().Be(0);
    }

    [Fact]
    public void sort_should_order_unbounded_bounds_consistently()
    {
        // given
        var ranges = new List<Range<string>> { new("3", "5"), new(null, "5"), new("3", null), new("1", "5") };

        // when
        ranges.Sort();

        // then - null lower bound first, null upper bound last
        ranges
            .Should()
            .ContainInOrder(
                new Range<string>(null, "5"),
                new Range<string>("1", "5"),
                new Range<string>("3", "5"),
                new Range<string>("3", null)
            );
    }

    #endregion
}
