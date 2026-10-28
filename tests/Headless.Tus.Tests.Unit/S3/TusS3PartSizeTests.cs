// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using tusdotnet.Models;

namespace Tests.S3;

public sealed class TusS3PartSizeTests : TestBase
{
    private const long _MiB = 1024 * 1024;
    private const long _GiB = 1024 * _MiB;

    // 10,000 parts less four claim windows of 64 at the defaults.
    private const long _DefaultPartBudget = 9_744;

    private static TusS3StoreOptions _Options() => new() { Bucket = "uploads-bucket" };

    [Fact]
    public void should_use_the_configured_part_size_for_a_deferred_length()
    {
        TusS3PartSize.ForUpload(null, _Options()).Should().Be((int)(8 * _MiB));
    }

    [Fact]
    public void should_use_the_configured_part_size_when_the_length_fits_the_budget()
    {
        TusS3PartSize.ForUpload(10 * _MiB, _Options()).Should().Be((int)(8 * _MiB));
    }

    [Fact]
    public void should_round_a_large_upload_part_size_up_to_a_whole_mib_within_the_budget()
    {
        // given - 200 GiB / 9,744 parts is about 21.02 MiB per part
        const long length = 200 * _GiB;

        // when
        var partSize = TusS3PartSize.ForUpload(length, _Options());

        // then
        partSize.Should().Be((int)(22 * _MiB));
        (partSize % _MiB).Should().Be(0);
        var partCount = (length + partSize - 1) / partSize;
        partCount.Should().BeLessThanOrEqualTo(_DefaultPartBudget);
    }

    [Fact]
    public void should_accept_the_largest_length_the_budget_allows_at_max_part_size()
    {
        TusS3PartSize.ForUpload(_DefaultPartBudget * 64 * _MiB, _Options()).Should().Be((int)(64 * _MiB));
    }

    [Fact]
    public void should_reject_a_length_needing_parts_above_max_part_size()
    {
        // given - one byte more than the budget holds at MaxPartSize; 10,000 x MaxPartSize is well above that
        const long length = (_DefaultPartBudget * 64 * _MiB) + 1;

        // when
        var act = () => TusS3PartSize.ForUpload(length, _Options());

        // then
        act.Should().Throw<TusStoreException>().WithMessage($"*{_DefaultPartBudget * 64 * _MiB} bytes*");
    }

    [Fact]
    public void should_reject_a_length_above_ten_thousand_max_size_parts()
    {
        var act = () => TusS3PartSize.ForUpload((10_000 * 64 * _MiB) + 1, _Options());

        act.Should().Throw<TusStoreException>();
    }

    [Fact]
    public void should_not_overflow_for_the_largest_length()
    {
        var act = () => TusS3PartSize.ForUpload(long.MaxValue, _Options());

        act.Should().Throw<TusStoreException>();
    }

    [Fact]
    public void should_cap_a_deferred_upload_at_the_part_budget_times_its_part_size()
    {
        TusS3PartSize
            .DeferredLengthCap((int)(8 * _MiB), partClaimWindow: 64)
            .Should()
            .Be(_DefaultPartBudget * 8 * _MiB);
    }

    [Fact]
    public void should_shrink_the_budget_as_the_claim_window_widens()
    {
        // 10,000 - 4 x 250
        TusS3PartSize.DeferredLengthCap((int)(8 * _MiB), partClaimWindow: 250).Should().Be(9_000 * 8 * _MiB);
    }
}
