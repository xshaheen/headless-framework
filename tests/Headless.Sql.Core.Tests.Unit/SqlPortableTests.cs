// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SqlPortableTests : TestBase
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 10)]
    [InlineData(15, 10)]
    [InlineData(-15, -10)]
    public void should_truncate_durations_toward_zero_at_microseconds(long ticks, long expected)
    {
        SqlPortable.Truncate(TimeSpan.FromTicks(ticks)).Ticks.Should().Be(expected);
    }
}
