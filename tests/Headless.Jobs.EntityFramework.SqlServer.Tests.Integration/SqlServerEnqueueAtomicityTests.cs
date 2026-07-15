// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the coordinated atomic-enqueue conformance suite against SQL Server.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerEnqueueAtomicityTests(SqlServerJobsCoordinationFixture fixture)
    : JobsEnqueueAtomicityConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task coordinated_save_hooks_isolate_tracked_entries_per_context()
    {
        return base.coordinated_save_hooks_isolate_tracked_entries_per_context();
    }

    [Fact]
    public override Task domain_message_and_job_commit_atomically()
    {
        return base.domain_message_and_job_commit_atomically();
    }

    [Fact]
    public override Task rollback_discards_domain_message_and_job()
    {
        return base.rollback_discards_domain_message_and_job();
    }

    [Fact]
    public override Task domain_write_and_enqueue_commit_atomically()
    {
        return base.domain_write_and_enqueue_commit_atomically();
    }

    [Fact]
    public override Task rollback_discards_enqueue_and_domain_write()
    {
        return base.rollback_discards_enqueue_and_domain_write();
    }

    [Fact]
    public override Task two_enqueues_in_one_scope_both_commit()
    {
        return base.two_enqueues_in_one_scope_both_commit();
    }

    [Fact]
    public override Task batch_enqueue_commits_atomically()
    {
        return base.batch_enqueue_commits_atomically();
    }

    [Fact]
    public override Task batch_enqueue_rolls_back()
    {
        return base.batch_enqueue_rolls_back();
    }

    [Fact]
    public override Task enqueue_without_coordinator_inserts_directly()
    {
        return base.enqueue_without_coordinator_inserts_directly();
    }

    [Fact]
    public override Task cron_enqueue_commits_atomically()
    {
        return base.cron_enqueue_commits_atomically();
    }

    [Fact]
    public override Task cron_enqueue_rolls_back()
    {
        return base.cron_enqueue_rolls_back();
    }

    [Fact]
    public override Task cron_batch_enqueue_rolls_back()
    {
        return base.cron_batch_enqueue_rolls_back();
    }
}
