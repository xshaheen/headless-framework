// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// The conformance scenarios that claim or purge with <c>READPAST</c>, re-run on a lease database with read committed
/// snapshot isolation on, where <c>READPAST</c> at READ COMMITTED is refused unless the provider adds
/// <c>READCOMMITTEDLOCK</c>.
/// </summary>
[Collection<SqlServerRcsiFencingFixture>]
public sealed class SqlServerRcsiLeasesConformanceTests(SqlServerRcsiFencingFixture fixture)
    : LeasesConformanceTests<SqlServerRcsiFencingFixture>(fixture)
{
    [Fact]
    public async Task should_run_against_a_lease_database_with_read_committed_snapshot_on()
    {
        (
            await Fixture.ScalarAsync(
                "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE database_id = DB_ID()",
                AbortToken
            )
        )
            .Should()
            .Be(1);
    }

    [Fact]
    public override Task should_take_over_an_expired_lease_and_leave_nothing_for_the_sweep()
    {
        return base.should_take_over_an_expired_lease_and_leave_nothing_for_the_sweep();
    }

    [Fact]
    public override Task should_report_stale_or_the_ended_state_when_renewing()
    {
        return base.should_report_stale_or_the_ended_state_when_renewing();
    }

    [Fact]
    public override Task should_make_a_grant_wait_for_an_open_fence_and_then_see_its_settlement()
    {
        return base.should_make_a_grant_wait_for_an_open_fence_and_then_see_its_settlement();
    }

    [Fact]
    public override Task should_hand_each_expired_lease_to_exactly_one_committed_handler_when_sweepers_race()
    {
        return base.should_hand_each_expired_lease_to_exactly_one_committed_handler_when_sweepers_race();
    }

    [Fact]
    public override Task should_roll_back_only_the_lease_whose_handler_threw_and_offer_it_again_later()
    {
        return base.should_roll_back_only_the_lease_whose_handler_threw_and_offer_it_again_later();
    }

    [Fact]
    public override Task should_not_reclaim_an_always_throwing_lease_within_one_sweep_call()
    {
        return base.should_not_reclaim_an_always_throwing_lease_within_one_sweep_call();
    }

    [Fact]
    public override Task should_sweep_only_the_requested_kind()
    {
        return base.should_sweep_only_the_requested_kind();
    }

    [Fact]
    public override Task should_purge_only_old_ended_leases_and_never_reissue_a_generation()
    {
        return base.should_purge_only_old_ended_leases_and_never_reissue_a_generation();
    }
}
