// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Internal;

namespace Tests;

/// <summary>
/// The cron-seed duplicate-row guarantee (#471) rests on <see cref="JobsSeedId.ForCronSeed"/> producing the SAME
/// primary key for the same function on every node — that is what makes two concurrent first-boot inserts collide on
/// the primary key instead of creating two rows. These tests pin that contract.
/// </summary>
public sealed class JobsSeedIdTests
{
    [Fact]
    public void ForCronSeed_is_deterministic_for_the_same_function()
    {
        JobsSeedId.ForCronSeed("ProcessPayments").Should().Be(JobsSeedId.ForCronSeed("ProcessPayments"));
    }

    [Fact]
    public void ForCronSeed_differs_across_functions()
    {
        JobsSeedId.ForCronSeed("ProcessPayments").Should().NotBe(JobsSeedId.ForCronSeed("SendReports"));
    }

    [Fact]
    public void ForCronSeed_is_not_empty()
    {
        JobsSeedId.ForCronSeed("ProcessPayments").Should().NotBe(Guid.Empty);
    }
}
