// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the run-now and delayed store-clock enqueue conformance suite against PostgreSQL.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlStoreClockEnqueueTests(PostgreSqlJobsCoordinationFixture fixture)
    : PostgreSqlStoreClockEnqueueTestsBase<PostgreSqlJobsCoordinationFixture>(fixture);

/// <summary>The PostgreSql overrides of the shared suite.</summary>
public abstract class PostgreSqlStoreClockEnqueueTestsBase<TFixture>(TFixture fixture)
    : JobsStoreClockEnqueueConformanceTests<TFixture>(fixture)
    where TFixture : PostgreSqlJobsCoordinationFixture
{
    [Fact]
    public override Task run_now_enqueue_from_a_host_ahead_of_the_store_is_stamped_by_the_store_and_runs_immediately()
    {
        return base.run_now_enqueue_from_a_host_ahead_of_the_store_is_stamped_by_the_store_and_runs_immediately();
    }

    [Fact]
    public override Task run_now_enqueue_from_a_host_behind_the_store_is_stamped_by_the_store_and_runs_immediately()
    {
        return base.run_now_enqueue_from_a_host_behind_the_store_is_stamped_by_the_store_and_runs_immediately();
    }

    [Fact]
    public override Task coordinated_run_now_enqueue_is_stamped_at_the_write_and_runs_immediately_after_a_slow_commit()
    {
        return base.coordinated_run_now_enqueue_is_stamped_at_the_write_and_runs_immediately_after_a_slow_commit();
    }

    [Fact]
    public override Task run_now_batch_roots_share_one_store_instant_and_an_absolute_root_is_kept()
    {
        return base.run_now_batch_roots_share_one_store_instant_and_an_absolute_root_is_kept();
    }

    [Fact]
    public override Task idempotent_run_now_enqueue_is_stamped_by_the_store_and_a_repeat_writes_nothing()
    {
        return base.idempotent_run_now_enqueue_is_stamped_by_the_store_and_a_repeat_writes_nothing();
    }

    [Fact]
    public override Task coordinated_idempotent_run_now_enqueue_is_stamped_at_the_write_and_a_repeat_writes_nothing()
    {
        return base.coordinated_idempotent_run_now_enqueue_is_stamped_at_the_write_and_a_repeat_writes_nothing();
    }

    [Fact]
    public override Task coordinated_idempotent_delayed_schedule_is_due_at_the_write_plus_the_delay_and_a_repeat_writes_nothing()
    {
        return base.coordinated_idempotent_delayed_schedule_is_due_at_the_write_plus_the_delay_and_a_repeat_writes_nothing();
    }

    [Fact]
    public override Task absolute_only_enqueue_reads_no_store_clock()
    {
        return base.absolute_only_enqueue_reads_no_store_clock();
    }

    [Fact]
    public override Task delayed_schedule_from_a_host_ahead_of_the_store_is_due_at_the_store_instant_plus_the_delay()
    {
        return base.delayed_schedule_from_a_host_ahead_of_the_store_is_due_at_the_store_instant_plus_the_delay();
    }

    [Fact]
    public override Task delayed_schedule_from_a_host_behind_the_store_is_due_at_the_store_instant_plus_the_delay()
    {
        return base.delayed_schedule_from_a_host_behind_the_store_is_due_at_the_store_instant_plus_the_delay();
    }

    [Fact]
    public override Task idempotent_delayed_schedule_is_due_at_the_store_instant_plus_the_delay()
    {
        return base.idempotent_delayed_schedule_is_due_at_the_store_instant_plus_the_delay();
    }

    [Fact]
    public override Task zero_delay_schedule_from_a_host_behind_the_store_runs_immediately()
    {
        return base.zero_delay_schedule_from_a_host_behind_the_store_runs_immediately();
    }

    [Fact]
    public override Task delay_that_overflows_the_store_instant_throws_and_writes_no_row()
    {
        return base.delay_that_overflows_the_store_instant_throws_and_writes_no_row();
    }

    [Fact]
    public override Task coordinated_overflowing_delay_throws_and_leaves_the_caller_transaction_usable()
    {
        return base.coordinated_overflowing_delay_throws_and_leaves_the_caller_transaction_usable();
    }

    [Fact]
    public override Task run_now_cron_occurrence_from_a_host_ahead_of_the_store_is_stamped_by_the_store_and_claimed_without_waiting()
    {
        return base.run_now_cron_occurrence_from_a_host_ahead_of_the_store_is_stamped_by_the_store_and_claimed_without_waiting();
    }
}
