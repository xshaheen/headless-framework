// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable xUnit1044 // Avoid using TheoryData type arguments that are not serializable
namespace Tests.Primitives;

public sealed class RangeTests
{
    #region Creation

    [Fact]
    public void should_create_date_range_when_ctor_provide_inorder_from_and_to()
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
    public void should_throw_exception_when_ctor_provide_inorder_from_and_to()
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
    public void should_return_true_when_value_inclusive_has_value_is_in_range(
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
    public void should_handle_unbounded_bounds_when_range_inclusive_has(
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
    public void should_return_true_when_value_exclusive_has_value_is_in_range(
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

    #region Value Half-Open Has

    public static readonly TheoryData<Range<int>, int, bool> ValueFromInclusiveToExclusiveHasData = new()
    {
        { new(1, 10), 5, true }, // Middle
        { new(1, 10), 1, true }, // From edge is inclusive
        { new(1, 10), 10, false }, // To edge is exclusive
        { new(1, 10), 0, false }, // Below
        { new(1, 10), 11, false }, // Above
    };

    [Theory]
    [MemberData(nameof(ValueFromInclusiveToExclusiveHasData))]
    public void should_include_lower_and_exclude_upper_bound_when_value_from_inclusive_to_exclusive_has(
        Range<int> range,
        int value,
        bool expected
    )
    {
        // when
        var result = range.FromInclusiveToExclusiveHas(value);

        // then
        result.Should().Be(expected);
    }

    public static readonly TheoryData<Range<int>, int, bool> ValueFromExclusiveToInclusiveHasData = new()
    {
        { new(1, 10), 5, true }, // Middle
        { new(1, 10), 1, false }, // From edge is exclusive
        { new(1, 10), 10, true }, // To edge is inclusive
        { new(1, 10), 0, false }, // Below
        { new(1, 10), 11, false }, // Above
    };

    [Theory]
    [MemberData(nameof(ValueFromExclusiveToInclusiveHasData))]
    public void should_exclude_lower_and_include_upper_bound_when_value_from_exclusive_to_inclusive_has(
        Range<int> range,
        int value,
        bool expected
    )
    {
        // when
        var result = range.FromExclusiveToInclusiveHas(value);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_return_false_for_null_even_when_value_from_exclusive_to_inclusive_has_unbounded_below()
    {
        // given - (-inf, "z"]: InclusiveHas treats a null value as contained, but the (From, To] variant
        // explicitly rejects null values — pin the asymmetry so it only changes deliberately.
        var range = new Range<string>(null, "z");

        // then
        range.InclusiveHas((string?)null).Should().BeTrue();
        range.FromExclusiveToInclusiveHas((string?)null).Should().BeFalse();
    }

    #endregion

    #region Range Half-Open Has

    public static readonly TheoryData<Range<string>, Range<string>, bool> RangeExclusiveHasData = new()
    {
        { new Range<string>("a", "z"), new Range<string>("b", "y"), true }, // Strictly inside
        { new Range<string>("a", "z"), new Range<string>("a", "y"), false }, // Shared lower edge rejected
        { new Range<string>("a", "z"), new Range<string>("b", "z"), false }, // Shared upper edge rejected
        { new Range<string>(null, "z"), new Range<string>("a", "y"), true }, // Unbounded-below outer strictly contains a bounded lower
        { new Range<string>(null, "z"), new Range<string>(null, "y"), false }, // Unbounded-below inner is never strictly inside
        { new Range<string>("a", null), new Range<string>("b", null), false }, // Unbounded-above inner is never strictly inside
        { new Range<string>("a", null), new Range<string>("b", "y"), true }, // Unbounded-above outer strictly contains a bounded upper
    };

    [Theory]
    [MemberData(nameof(RangeExclusiveHasData))]
    public void should_require_strict_containment_on_both_sides_when_range_exclusive_has(
        Range<string> range,
        Range<string> other,
        bool expected
    )
    {
        // when
        var result = range.ExclusiveHas(other);

        // then
        result.Should().Be(expected);
    }

    public static readonly TheoryData<Range<string>, Range<string>, bool> RangeInRangeLowerInclusiveData = new()
    {
        { new Range<string>("a", "z"), new Range<string>("a", "y"), true }, // Shared lower edge allowed
        { new Range<string>("a", "z"), new Range<string>("b", "z"), false }, // Shared upper edge rejected
        { new Range<string>(null, "z"), new Range<string>(null, "y"), true }, // Unbounded-below inner allowed inside unbounded-below outer
        { new Range<string>("a", null), new Range<string>("a", null), false }, // Unbounded-above inner rejected (upper must be strictly inside)
    };

    [Theory]
    [MemberData(nameof(RangeInRangeLowerInclusiveData))]
    public void should_allow_shared_lower_and_reject_shared_upper_bound_when_range_in_range_lower_inclusive(
        Range<string> range,
        Range<string> other,
        bool expected
    )
    {
        // when
        var result = range.InRangeLowerInclusive(other);

        // then
        result.Should().Be(expected);
    }

    public static readonly TheoryData<Range<string>, Range<string>, bool> RangeFromExclusiveToInclusiveHasData = new()
    {
        { new Range<string>("a", "z"), new Range<string>("b", "z"), true }, // Shared upper edge allowed
        { new Range<string>("a", "z"), new Range<string>("a", "y"), false }, // Shared lower edge rejected
        { new Range<string>(null, "z"), new Range<string>("a", "z"), true }, // Unbounded-below outer contains a bounded lower
        { new Range<string>("a", null), new Range<string>("b", null), true }, // Unbounded-above inner allowed inside unbounded-above outer
    };

    [Theory]
    [MemberData(nameof(RangeFromExclusiveToInclusiveHasData))]
    public void should_allow_shared_upper_and_reject_shared_lower_bound_when_range_from_exclusive_to_inclusive_has(
        Range<string> range,
        Range<string> other,
        bool expected
    )
    {
        // when
        var result = range.FromExclusiveToInclusiveHas(other);

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
    public void should_return_true_when_is_overlap_ranges_overlap(Range<int> range, Range<int> other, bool expected)
    {
        // when
        var result = range.IsOverlap(other);

        // then
        result.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(IsOverlapUnboundedData))]
    public void should_handle_unbounded_bounds_when_is_overlap(Range<string> range, Range<string> other, bool expected)
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
    public void should_return_ranges_without_overlap_when_remove_overlap(
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
    public void should_preserve_unbounded_remainders_when_remove_overlap(
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
    public void should_sort_unbounded_lower_bound_before_any_value_when_compare_to()
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
    public void should_sort_unbounded_upper_bound_after_any_value_when_compare_to()
    {
        // given - ["3", +inf) vs ["3", "5"]; an unbounded upper bound must sort last
        var unboundedAbove = new Range<string>("3", null);
        var bounded = new Range<string>("3", "5");

        // then
        unboundedAbove.CompareTo(bounded).Should().BePositive();
        bounded.CompareTo(unboundedAbove).Should().BeNegative();
    }

    [Fact]
    public void should_be_consistent_with_equals_for_unbounded_ranges_when_compare_to()
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
    public void should_order_unbounded_bounds_consistently_when_sort()
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
