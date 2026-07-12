// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the coordinated atomic-enqueue conformance suite against Postgres.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlEnqueueAtomicityTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsEnqueueAtomicityConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task domain_message_and_job_commit_atomically() => base.domain_message_and_job_commit_atomically();

    [Fact]
    public override Task rollback_discards_domain_message_and_job() => base.rollback_discards_domain_message_and_job();

    [Fact]
    public override Task domain_write_and_enqueue_commit_atomically() =>
        base.domain_write_and_enqueue_commit_atomically();

    [Fact]
    public override Task rollback_discards_enqueue_and_domain_write() =>
        base.rollback_discards_enqueue_and_domain_write();

    [Fact]
    public override Task two_enqueues_in_one_scope_both_commit() => base.two_enqueues_in_one_scope_both_commit();

    [Fact]
    public override Task batch_enqueue_commits_atomically() => base.batch_enqueue_commits_atomically();

    [Fact]
    public override Task batch_enqueue_rolls_back() => base.batch_enqueue_rolls_back();

    [Fact]
    public override Task enqueue_without_coordinator_inserts_directly() =>
        base.enqueue_without_coordinator_inserts_directly();

    [Fact]
    public override Task cron_enqueue_commits_atomically() => base.cron_enqueue_commits_atomically();

    [Fact]
    public override Task cron_enqueue_rolls_back() => base.cron_enqueue_rolls_back();

    [Fact]
    public override Task cron_batch_enqueue_rolls_back() => base.cron_batch_enqueue_rolls_back();
}
