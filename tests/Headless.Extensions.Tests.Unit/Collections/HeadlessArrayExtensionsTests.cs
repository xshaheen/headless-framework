// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Collections;

public sealed class HeadlessArrayExtensionsTests
{
    [Fact]
    public void should_bind_whole_array_reverse_to_linq_without_mutating_the_array()
    {
        // given
        int[] values = [1, 2, 3];

        // when
        var reversed = values.Reverse();

        // then
        reversed.Should().Equal(3, 2, 1);
        values.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void should_reverse_a_range_in_place()
    {
        // given
        int[] values = [1, 2, 3, 4];

        // when
        values.Reverse(1, 2);

        // then
        values.Should().Equal(1, 3, 2, 4);
    }
}
