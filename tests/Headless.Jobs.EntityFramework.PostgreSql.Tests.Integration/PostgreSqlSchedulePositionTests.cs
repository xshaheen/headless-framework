// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the cron schedule-position advance conformance suite against PostgreSQL.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlSchedulePositionTests
    : JobsSchedulePositionConformanceTests<PostgreSqlJobsCoordinationFixture>
{
    private readonly PostgreSqlJobsCoordinationFixture _fixture;

    public PostgreSqlSchedulePositionTests(PostgreSqlJobsCoordinationFixture fixture)
        : base(fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public override Task advance_from_the_observed_watermark_persists_the_new_position()
    {
        return base.advance_from_the_observed_watermark_persists_the_new_position();
    }

    [Fact]
    public override Task advance_with_a_stale_watermark_changes_nothing()
    {
        return base.advance_with_a_stale_watermark_changes_nothing();
    }

    [Fact]
    public override Task advance_with_a_stale_schedule_revision_changes_nothing()
    {
        return base.advance_with_a_stale_schedule_revision_changes_nothing();
    }

    [Fact]
    public override Task advance_against_a_paused_definition_changes_nothing()
    {
        return base.advance_against_a_paused_definition_changes_nothing();
    }

    [Fact]
    public override Task concurrent_advances_from_the_same_watermark_produce_exactly_one_winner()
    {
        return base.concurrent_advances_from_the_same_watermark_produce_exactly_one_winner();
    }

    [Fact]
    public override Task advancing_one_definition_leaves_a_sibling_untouched()
    {
        return base.advancing_one_definition_leaves_a_sibling_untouched();
    }

    [Fact]
    public override Task due_ness_and_the_returned_instant_follow_the_database_clock_not_a_skewed_node_clock()
    {
        return base.due_ness_and_the_returned_instant_follow_the_database_clock_not_a_skewed_node_clock();
    }

    [Fact]
    public override Task queueing_an_instant_with_a_terminal_occurrence_materializes_nothing()
    {
        return base.queueing_an_instant_with_a_terminal_occurrence_materializes_nothing();
    }

    [Fact]
    public override Task migrate_resets_the_position_when_the_code_defined_expression_changes()
    {
        return base.migrate_resets_the_position_when_the_code_defined_expression_changes();
    }

    [Fact]
    public override Task dispatch_selection_excludes_definitions_with_durable_fingerprint_defer_state()
    {
        return base.dispatch_selection_excludes_definitions_with_durable_fingerprint_defer_state();
    }

    [Fact]
    public override Task materialization_survives_restart_and_the_idle_occurrence_is_claimed_later() =>
        base.materialization_survives_restart_and_the_idle_occurrence_is_claimed_later();

    [Fact]
    public override Task concurrent_materializations_commit_one_position_and_one_occurrence() =>
        base.concurrent_materializations_commit_one_position_and_one_occurrence();

    [Fact]
    public override Task terminal_occurrence_is_an_explicit_committed_outcome_without_rematerialization() =>
        base.terminal_occurrence_is_an_explicit_committed_outcome_without_rematerialization();

    [Fact]
    public override Task existing_non_terminal_occurrence_is_reused_and_position_advances() =>
        base.existing_non_terminal_occurrence_is_reused_and_position_advances();

    [Fact]
    public override Task failure_after_the_position_update_rolls_back_position_and_occurrence() =>
        base.failure_after_the_position_update_rolls_back_position_and_occurrence();

    [Fact]
    public override Task stale_and_future_materializations_are_distinct_no_mutation_outcomes() =>
        base.stale_and_future_materializations_are_distinct_no_mutation_outcomes();

    [Fact]
    public override Task cancellation_before_materialization_changes_neither_position_nor_occurrences() =>
        base.cancellation_before_materialization_changes_neither_position_nor_occurrences();

    [Fact]
    public async Task materialization_authorizes_due_ness_before_its_transaction_waits_on_the_definition_lock()
    {
        var ct = AbortToken;
        await _fixture.ResetDatabaseAsync(ct);
        var capture = new LeaseSqlCapture();
        using var host = _fixture.BuildInterceptedHost("materialize-pg-lock", capture);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var cronId = Guid.NewGuid();
        await _fixture.SeedCronJobAsync(
            cronId,
            "materialize-pg-lock",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -60,
            nextDueOffsetSeconds: 1
        );
        var seeded = await _fixture.ReadCronSchedulePositionAsync(cronId, ct);

        await using var blocker = new NpgsqlConnection(_fixture.ConnectionString);
        await blocker.OpenAsync(ct);
        await using var blockerTransaction = await blocker.BeginTransactionAsync(ct);
        await using (
            var command = new NpgsqlCommand(
                "SELECT \"Id\" FROM jobs.\"CronJobs\" WHERE \"Id\" = @id FOR UPDATE;",
                blocker,
                blockerTransaction
            )
        )
        {
            command.Parameters.AddWithValue("id", cronId);
            await command.ExecuteScalarAsync(ct);
        }

        capture.Clear();
        var untilDue = seeded.NextDueUtc - DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
        if (untilDue > TimeSpan.Zero)
        {
            await Task.Delay(untilDue, ct);
        }

        var materialization = new CronScheduleMaterialization
        {
            Advance = new CronScheduleAdvance
            {
                CronJobId = cronId,
                ObservedReconciledThroughUtc = seeded.ReconciledThroughUtc,
                ExpectedScheduleRevision = 0,
                ReconciledThroughUtc = seeded.NextDueUtc,
                NextDueUtc = seeded.NextDueUtc.AddMinutes(1),
                RequireProjectionDue = true,
            },
            ExecutionTimeUtc = seeded.NextDueUtc,
        };
        var materializationTask = persistence.MaterializeCronScheduleOccurrenceAsync(materialization, ct);
        var updateObserved = SpinWait.SpinUntil(
            () =>
                capture.Statements.Any(statement =>
                    statement.InExplicitTransaction
                    && statement.Sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                    && statement.Sql.Contains("NextDueUtc", StringComparison.Ordinal)
                ),
            TimeSpan.FromSeconds(5)
        );
        var waitedForDefinitionLock = !materializationTask.IsCompleted;

        await blockerTransaction.CommitAsync(ct);
        var result = await materializationTask;

        updateObserved.Should().BeTrue("the materialization UPDATE must reach the held row lock");
        waitedForDefinitionLock.Should().BeTrue("the materialization transaction must wait for the definition lock");
        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceCreated);
        capture
            .Statements.Should()
            .Contain(statement =>
                !statement.InExplicitTransaction
                && statement.Sql.Contains("NextDueUtc", StringComparison.Ordinal)
                && statement.Sql.Contains("now()", StringComparison.OrdinalIgnoreCase)
            );
    }
}
