// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Tests;

[Collection<JobsKeyLockPostgreSqlFixture>]
public sealed class PostgreSqlCronMaterializationTests(JobsKeyLockPostgreSqlFixture fixture)
    : JobsCronMaterializationConformanceTests(options => options.UseNpgsql(fixture.ConnectionString))
{
    [Fact]
    public override Task bulk_reads_definitions_once_after_ordered_locks() =>
        base.bulk_reads_definitions_once_after_ordered_locks();

    [Fact]
    public override Task empty_batch_issues_no_definition_commands() =>
        base.empty_batch_issues_no_definition_commands();

    [Fact]
    public override Task missing_definition_rolls_back_and_leaves_inputs_unchanged() =>
        base.missing_definition_rolls_back_and_leaves_inputs_unchanged();

    [Fact]
    public override Task retry_rebuilds_context_candidates_and_definition_snapshots() =>
        base.retry_rebuilds_context_candidates_and_definition_snapshots();

    [Fact]
    public override Task definition_edit_waits_until_the_complete_snapshot_batch_commits() =>
        base.definition_edit_waits_until_the_complete_snapshot_batch_commits();
}
