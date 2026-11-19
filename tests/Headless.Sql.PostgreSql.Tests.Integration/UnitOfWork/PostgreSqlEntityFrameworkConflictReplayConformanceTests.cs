// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>Runs the EF Core conflict-replay conformance suite against <c>RunAsync(DbContext, …)</c> over Npgsql.</summary>
[Collection<PostgreSqlUnitOfWorkFixture>]
public sealed class PostgreSqlEntityFrameworkConflictReplayConformanceTests(PostgreSqlUnitOfWorkFixture fixture)
    : EntityFrameworkConflictReplayConformanceTests(
        new EntityFrameworkConflictReplayFixture(
            options => options.UseNpgsql(fixture.ConnectionString),
            "CREATE TABLE IF NOT EXISTS conflict_counters (id serial PRIMARY KEY, counter int NOT NULL, version int NOT NULL); DELETE FROM conflict_counters;"
        )
    )
{
    [Fact]
    public override Task should_apply_every_increment_when_ten_opted_in_blocks_contend_for_one_row()
    {
        return base.should_apply_every_increment_when_ten_opted_in_blocks_contend_for_one_row();
    }

    [Fact]
    public override Task should_fail_the_losing_blocks_when_the_contending_blocks_did_not_opt_in()
    {
        return base.should_fail_the_losing_blocks_when_the_contending_blocks_did_not_opt_in();
    }

    [Fact]
    public override Task should_surface_the_conflict_when_the_block_still_conflicts_on_its_last_attempt()
    {
        return base.should_surface_the_conflict_when_the_block_still_conflicts_on_its_last_attempt();
    }
}
