// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Internal;

namespace Tests;

/// <summary>
/// The cron-seed duplicate-row guarantee (#471) rests on <see cref="JobsSeedId.ForCronSeed"/> producing the SAME
/// primary key for the same owner and function on every node — that is what makes two concurrent first-boot inserts
/// collide on the primary key instead of creating two rows. The owner scoping rests on the marker prefix. These tests
/// pin both contracts.
/// </summary>
public sealed class JobsSeedIdTests
{
    [Fact]
    public void for_cron_seed_is_deterministic_for_the_same_owner_and_function()
    {
        JobsSeedId
            .ForCronSeed("billing", "ProcessPayments")
            .Should()
            .Be(JobsSeedId.ForCronSeed("billing", "ProcessPayments"));
    }

    [Fact]
    public void for_cron_seed_differs_across_functions()
    {
        JobsSeedId
            .ForCronSeed("billing", "ProcessPayments")
            .Should()
            .NotBe(JobsSeedId.ForCronSeed("billing", "SendReports"));
    }

    [Fact]
    public void for_cron_seed_differs_across_owners_of_the_same_function()
    {
        JobsSeedId
            .ForCronSeed("billing", "ProcessPayments")
            .Should()
            .NotBe(JobsSeedId.ForCronSeed("reports", "ProcessPayments"));
    }

    [Fact]
    public void for_cron_seed_is_not_empty()
    {
        JobsSeedId.ForCronSeed("billing", "ProcessPayments").Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void cron_seed_marker_starts_with_the_owner_prefix()
    {
        JobsSeedId
            .CronSeedMarker("billing", "ProcessPayments")
            .Should()
            .Be("headless.jobs.seed:billing:cron:ProcessPayments");
        JobsSeedId.OwnerMarkerPrefix("billing").Should().Be("headless.jobs.seed:billing:");
    }

    [Fact]
    public void default_owner_is_one_shared_constant()
    {
        JobsSeedId.DefaultOwner.Should().Be("default");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("billing:eu")]
    public void validate_owner_rejects_a_blank_owner_or_one_containing_a_colon(string owner)
    {
        var act = () => JobsSeedId.ValidateOwner(owner);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void validate_owner_rejects_null()
    {
        var act = () => JobsSeedId.ValidateOwner(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void validate_entries_rejects_an_entry_owned_by_another_owner()
    {
        var act = () => JobsSeedId.ValidateEntries("billing", [_Entry("reports", "ProcessPayments")]);

        act.Should().Throw<ArgumentException>().WithMessage("*not owned by seed owner 'billing'*");
    }

    [Fact]
    public void validate_entries_rejects_two_entries_with_one_id()
    {
        var entry = _Entry("billing", "ProcessPayments");

        var act = () => JobsSeedId.ValidateEntries("billing", [entry, entry]);

        act.Should().Throw<ArgumentException>().WithMessage("*more than once*");
    }

    [Fact]
    public void validate_entries_accepts_distinct_entries_of_the_owner()
    {
        var act = () =>
            JobsSeedId.ValidateEntries(
                "billing",
                [_Entry("billing", "ProcessPayments"), _Entry("billing", "SendReports")]
            );

        act.Should().NotThrow();
    }

    private static CronSeedDefinition _Entry(string owner, string function)
    {
        return CronSeedDefinition.ForCronFunction(
            owner,
            function,
            "0 * * * * *",
            MissedRunPolicy.Coalesce,
            JobsRecoveryDefaults.MissedRunGraceSeconds,
            CronOverlapPolicy.Allow
        );
    }
}
