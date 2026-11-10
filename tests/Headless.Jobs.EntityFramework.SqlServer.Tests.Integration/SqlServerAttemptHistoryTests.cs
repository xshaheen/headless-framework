// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the attempt-history conformance suite against SqlServer.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerAttemptHistoryTests(SqlServerJobsCoordinationFixture fixture)
    : JobsAttemptHistoryConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task retry_writes_and_the_success_write_close_three_attempts_in_order()
    {
        return base.retry_writes_and_the_success_write_close_three_attempts_in_order();
    }

    [Fact]
    public override Task store_failing_a_run_of_a_dead_node_leaves_no_open_attempt()
    {
        return base.store_failing_a_run_of_a_dead_node_leaves_no_open_attempt();
    }

    [Fact]
    public override Task node_death_reclaim_to_idle_closes_the_attempt_as_lost_and_the_next_claim_opens_another()
    {
        return base.node_death_reclaim_to_idle_closes_the_attempt_as_lost_and_the_next_claim_opens_another();
    }

    [Fact]
    public override Task skip_reclaim_stalled_lease_and_shutdown_release_each_close_the_attempt_with_their_outcome()
    {
        return base.skip_reclaim_stalled_lease_and_shutdown_release_each_close_the_attempt_with_their_outcome();
    }

    [Fact]
    public override Task a_close_with_no_open_attempt_inserts_one_closed_attempt_with_what_it_knows()
    {
        return base.a_close_with_no_open_attempt_inserts_one_closed_attempt_with_what_it_knows();
    }

    [Fact]
    public override Task a_graceful_release_of_a_run_that_never_started_inserts_no_attempt()
    {
        return base.a_graceful_release_of_a_run_that_never_started_inserts_no_attempt();
    }

    [Fact]
    public override Task deleting_a_chain_deletes_every_members_attempts()
    {
        return base.deleting_a_chain_deletes_every_members_attempts();
    }

    [Fact]
    public override Task deleting_a_cron_job_deletes_its_occurrences_attempts()
    {
        return base.deleting_a_cron_job_deletes_its_occurrences_attempts();
    }

    [Fact]
    public override Task an_attempt_closes_once_when_two_writers_race_to_end_a_run()
    {
        return base.an_attempt_closes_once_when_two_writers_race_to_end_a_run();
    }
}
