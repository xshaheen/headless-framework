// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus.Internal;

namespace Tests.S3;

public sealed class TusS3ConcatPlanTests : TestBase
{
    private const long _MiB = 1024 * 1024;

    [Fact]
    public void should_copy_parts_inside_one_partial_and_stream_parts_that_span_partials()
    {
        // given - 3 MiB, 13 MiB, and 0.5 MiB partials in 5 MiB parts: 16.5 MiB in total
        long[] lengths = [3 * _MiB, 13 * _MiB, _MiB / 2];

        // when
        var plan = TusS3ConcatPlan.Create(lengths, 5 * _MiB);

        // then
        plan.Should().HaveCount(4);

        plan[0].Number.Should().Be(1);
        plan[0].Length.Should().Be(5 * _MiB);
        plan[0].IsCopy.Should().BeFalse();
        plan[0].Sources.Should().Equal(new TusS3ConcatSource(0, 0, 3 * _MiB), new TusS3ConcatSource(1, 0, 2 * _MiB));

        plan[1].Sources.Should().Equal(new TusS3ConcatSource(1, 2 * _MiB, 5 * _MiB));
        plan[1].IsCopy.Should().BeTrue();

        plan[2].Sources.Should().Equal(new TusS3ConcatSource(1, 7 * _MiB, 5 * _MiB));
        plan[2].IsCopy.Should().BeTrue();

        plan[3].Number.Should().Be(4);
        plan[3].Length.Should().Be(_MiB + (_MiB / 2));
        plan[3]
            .Sources.Should()
            .Equal(new TusS3ConcatSource(1, 12 * _MiB, _MiB), new TusS3ConcatSource(2, 0, _MiB / 2));
    }

    [Fact]
    public void should_copy_every_part_when_partial_boundaries_align_with_the_part_size()
    {
        // given
        long[] lengths = [5 * _MiB, 10 * _MiB, 2 * _MiB];

        // when
        var plan = TusS3ConcatPlan.Create(lengths, 5 * _MiB);

        // then
        plan.Should().OnlyContain(part => part.IsCopy);
        plan.Select(part => part.Sources[0])
            .Should()
            .Equal(
                new TusS3ConcatSource(0, 0, 5 * _MiB),
                new TusS3ConcatSource(1, 0, 5 * _MiB),
                new TusS3ConcatSource(1, 5 * _MiB, 5 * _MiB),
                new TusS3ConcatSource(2, 0, 2 * _MiB)
            );
        plan.Select(part => part.Number).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void should_make_every_part_but_the_last_exactly_the_part_size()
    {
        // given - lengths that leave a short last part
        long[] lengths = [7 * _MiB, 1, 6 * _MiB, 3 * _MiB];

        // when
        var plan = TusS3ConcatPlan.Create(lengths, 5 * _MiB);

        // then - 16 MiB + 1 byte: three full parts and a 1 MiB + 1 byte last part
        plan.Select(part => part.Length).Should().Equal(5 * _MiB, 5 * _MiB, 5 * _MiB, _MiB + 1);
        plan.Should().OnlyContain(part => part.Sources.Sum(source => source.Length) == part.Length);
    }

    [Fact]
    public void should_cover_every_byte_of_the_partials_in_order()
    {
        // given
        long[] lengths = [1, (5 * _MiB) - 1, 4 * _MiB, (9 * _MiB) + 3, 17];

        // when
        var plan = TusS3ConcatPlan.Create(lengths, 5 * _MiB);

        // then - walking the sources visits each partial's bytes once, front to back
        var expectedIndex = 0;
        var expectedOffset = 0L;

        foreach (var source in plan.SelectMany(part => part.Sources))
        {
            if (source.Offset == 0 && source.PartialIndex != expectedIndex)
            {
                expectedOffset.Should().Be(lengths[expectedIndex]);
                expectedIndex = source.PartialIndex;
                expectedOffset = 0;
            }

            source.PartialIndex.Should().Be(expectedIndex);
            source.Offset.Should().Be(expectedOffset);
            expectedOffset += source.Length;
        }

        expectedIndex.Should().Be(lengths.Length - 1);
        expectedOffset.Should().Be(lengths[^1]);
    }

    [Fact]
    public void should_copy_a_single_partial_in_part_sized_ranges()
    {
        // when
        var plan = TusS3ConcatPlan.Create([12 * _MiB], 5 * _MiB);

        // then
        plan.Should().OnlyContain(part => part.IsCopy);
        plan.Select(part => (part.Sources[0].Offset, part.Length))
            .Should()
            .Equal((0, 5 * _MiB), (5 * _MiB, 5 * _MiB), (10 * _MiB, 2 * _MiB));
    }

    [Fact]
    public void should_skip_empty_partials()
    {
        // given - empty partials around and between the ones holding bytes
        long[] lengths = [0, 3 * _MiB, 0, 0, 4 * _MiB, 0];

        // when
        var plan = TusS3ConcatPlan.Create(lengths, 5 * _MiB);

        // then
        plan.Should().HaveCount(2);
        plan[0].Sources.Should().Equal(new TusS3ConcatSource(1, 0, 3 * _MiB), new TusS3ConcatSource(4, 0, 2 * _MiB));
        plan[1].Sources.Should().Equal(new TusS3ConcatSource(4, 2 * _MiB, 2 * _MiB));
        plan[1].IsCopy.Should().BeTrue();
    }

    [Fact]
    public void should_plan_no_parts_when_every_partial_is_empty()
    {
        TusS3ConcatPlan.Create([0, 0, 0], 5 * _MiB).Should().BeEmpty();
    }

    [Fact]
    public void should_reject_a_negative_partial_length()
    {
        var act = () => TusS3ConcatPlan.Create([5, -1], 5 * _MiB);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
