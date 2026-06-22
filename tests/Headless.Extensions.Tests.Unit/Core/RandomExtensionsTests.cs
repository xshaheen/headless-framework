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
}
