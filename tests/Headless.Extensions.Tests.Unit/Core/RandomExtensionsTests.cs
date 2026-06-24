// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Core;

public sealed class RandomExtensionsTests
{
    [Fact]
    public void next_decimal_with_default_full_range_should_not_overflow()
    {
        // given - default bounds span the entire decimal domain; the previous (max - min) form overflowed
        var random = new Random(12345);

        // when
        var act = () => random.NextDecimal();

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void next_decimal_should_stay_within_the_requested_bounds()
    {
        // given
        var random = new Random(12345);

        // when / then
        for (var i = 0; i < 200; i++)
        {
            random.NextDecimal(-100m, 100m).Should().BeInRange(-100m, 100m);
        }
    }

    [Fact]
    public void next_uint64_with_wide_range_should_stay_within_bounds()
    {
        // given - a range whose (max - min) multiplied by a large sample overflowed the old ulong multiply
        var random = new Random(12345);
        const ulong min = 100ul;
        const ulong max = ulong.MaxValue - 100ul;

        // when / then
        for (var i = 0; i < 1000; i++)
        {
            random.NextUInt64(min, max).Should().BeGreaterThanOrEqualTo(min).And.BeLessThan(max);
        }
    }

    [Fact]
    public void next_uint64_with_equal_bounds_should_return_min()
    {
        // given
        var random = new Random(12345);

        // when / then
        random.NextUInt64(42ul, 42ul).Should().Be(42ul);
    }

    [Fact]
    public void next_int64_with_full_range_should_not_overflow()
    {
        // given - the previous (max - min) form overflowed for the full long span
        var random = new Random(12345);

        // when
        var act = () => random.NextInt64(long.MinValue, long.MaxValue);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void next_int64_should_stay_within_the_requested_bounds()
    {
        // given
        var random = new Random(12345);

        // when / then
        for (var i = 0; i < 1000; i++)
        {
            random.NextInt64(-1000L, 1000L).Should().BeGreaterThanOrEqualTo(-1000L).And.BeLessThan(1000L);
        }
    }

    [Fact]
    public void next_int64_with_equal_bounds_should_return_min()
    {
        // given
        var random = new Random(12345);

        // when / then
        random.NextInt64(7L, 7L).Should().Be(7L);
    }
}
