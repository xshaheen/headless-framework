// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Extensions.System;

public sealed class ComparableExtensionsTests
{
    [Fact]
    public void InclusiveBetween_tests()
    {
        // Number
        const int number = 5;
        number.InclusiveBetween(1, 10).Should().Be(true);
        number.InclusiveBetween(1, 5).Should().Be(true);
        number.InclusiveBetween(5, 10).Should().Be(true);
        number.InclusiveBetween(10, 20).Should().Be(false);

        // DateTime
        var dateTimeValue = new DateTime(2014, 10, 4, 18, 20, 42, 0);

        dateTimeValue.InclusiveBetween(new DateTime(2014, 1, 1), new DateTime(2015, 1, 1)).Should().Be(true);

        dateTimeValue.InclusiveBetween(new DateTime(2015, 1, 1), new DateTime(2016, 1, 1)).Should().Be(false);
    }

    [Fact]
    public void ExclusiveBetween_tests()
    {
        // Number
        const int number = 5;
        number.ExclusiveBetween(1, 10).Should().Be(true);
        number.ExclusiveBetween(1, 5).Should().Be(false);
        number.ExclusiveBetween(5, 10).Should().Be(false);
        number.ExclusiveBetween(10, 20).Should().Be(false);

        // DateTime
        var dateTimeValue = new DateTime(2014, 10, 4, 18, 20, 42, 0);

        dateTimeValue.ExclusiveBetween(new DateTime(2014, 1, 1), new DateTime(2015, 1, 1)).Should().Be(true);

        dateTimeValue.ExclusiveBetween(new DateTime(2015, 1, 1), new DateTime(2016, 1, 1)).Should().Be(false);
    }
}
