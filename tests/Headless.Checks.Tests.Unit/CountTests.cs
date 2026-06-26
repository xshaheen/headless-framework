// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class CountTests
{
    private static readonly List<int> _Three = [1, 2, 3];

    [Fact]
    public void has_count_should_return_collection_when_exact()
    {
        Argument.HasCount(_Three, 3).Should().BeSameAs(_Three);
    }

    [Fact]
    public void has_count_should_throw_when_not_exact()
    {
        var collection = _Three;
        var action = () => Argument.HasCount(collection, 2);

        action
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage(
                "The argument \"collection\" must contain exactly 2 item(s) (Actual count 3). (Parameter 'collection')"
            );
    }

    [Fact]
    public void has_count_should_throw_argument_null_when_null()
    {
        var action = () => Argument.HasCount((IReadOnlyCollection<int>?)null, 0);
        action.Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void has_count_should_work_on_lazy_enumerable()
    {
        IEnumerable<int> lazy = _Three.Where(x => x > 0);

        Argument.HasCount(lazy, 3).Should().BeSameAs(lazy);

        var action = () => Argument.HasCount(_Three.Where(x => x > 1), 3);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void has_min_count_should_validate_lower_bound()
    {
        Argument.HasMinCount(_Three, 3).Should().BeSameAs(_Three);
        Argument.HasMinCount(_Three, 1).Should().BeSameAs(_Three);

        var action = () => Argument.HasMinCount(_Three, 4);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>().WithMessage("*at least 4*Actual count 3*");
    }

    [Fact]
    public void has_max_count_should_validate_upper_bound()
    {
        Argument.HasMaxCount(_Three, 3).Should().BeSameAs(_Three);
        Argument.HasMaxCount(_Three, 5).Should().BeSameAs(_Three);

        var action = () => Argument.HasMaxCount(_Three, 2);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>().WithMessage("*at most 2*Actual count 3*");
    }

    [Theory]
    [InlineData(2, 4)]
    [InlineData(3, 3)]
    [InlineData(1, 10)]
    public void has_count_between_should_return_collection_when_in_range(int min, int max)
    {
        Argument.HasCountBetween(_Three, min, max).Should().BeSameAs(_Three);
    }

    [Theory]
    [InlineData(4, 6)]
    [InlineData(0, 2)]
    public void has_count_between_should_throw_when_out_of_range(int min, int max)
    {
        var action = () => Argument.HasCountBetween(_Three, min, max);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void has_count_between_should_throw_when_bounds_inverted()
    {
        var action = () => Argument.HasCountBetween(_Three, 5, 2);
        action.Should().ThrowExactly<ArgumentException>();
    }
}
