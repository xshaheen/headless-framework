// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerKeyedSchedulingTests(SqlServerJobsCoordinationFixture fixture)
    : JobsKeyedSchedulingConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task keyed_provider_operation_matrix_survives_restart() =>
        base.keyed_provider_operation_matrix_survives_restart();

    [Fact]
    public override Task keyed_constraints_follow_custom_column_mappings() =>
        base.keyed_constraints_follow_custom_column_mappings();

    [Fact]
    public override Task fresh_schema_enforces_keyed_metadata_and_scoped_uniqueness() =>
        base.fresh_schema_enforces_keyed_metadata_and_scoped_uniqueness();

    [Fact]
    public override Task manual_job_configuration_requires_explicit_ordinal_scope() =>
        base.manual_job_configuration_requires_explicit_ordinal_scope();

    [Fact]
    public override Task coordinated_manual_nonordinal_model_rejects_keyed_operations_before_middleware() =>
        base.coordinated_manual_nonordinal_model_rejects_keyed_operations_before_middleware();

    [Fact]
    public override Task manual_keyed_constraints_follow_custom_column_mappings() =>
        base.manual_keyed_constraints_follow_custom_column_mappings();

    [Fact]
    public override Task manual_keyed_configuration_requires_finalization() =>
        base.manual_keyed_configuration_requires_finalization();

    [Fact]
    public override Task coordinated_add_rejects_retained_keyed_parent_before_batch_effects() =>
        base.coordinated_add_rejects_retained_keyed_parent_before_batch_effects();

    [Fact]
    public async Task ordinary_chain_add_retries_known_rollback_without_accepting_the_graph()
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        var commits = new FailFirstCommit();
        using var host = Fixture.BuildCoordinatedEnqueueHost<JobsDbContext>(
            "ordinary-chain-retry",
            db =>
                db.UseSqlServer(
                        Fixture.ConnectionString,
                        sql => sql.EnableRetryOnFailure(1, TimeSpan.Zero, errorNumbersToAdd: null)
                    )
                    .AddInterceptors(commits)
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, AbortToken);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var parent = JobsKeyedSchedulingScenarios.Candidate();
        var child = JobsKeyedSchedulingScenarios.Candidate();
        parent.Children.Add(child);
        commits.Armed = true;

        (await store.AddTimeJobsAsync([parent], AbortToken)).Should().Be(2);
        commits.Attempts.Should().Be(2);
        await using var context = await host
            .Services.GetRequiredService<IDbContextFactory<JobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        (await context.Set<TimeJobEntity>().CountAsync(AbortToken)).Should().Be(2);
        (await context.Set<TimeJobEntity>().SingleAsync(row => row.Id == child.Id, AbortToken))
            .ParentId.Should()
            .Be(parent.Id);
    }

    private sealed class FailFirstCommit : DbTransactionInterceptor
    {
        public bool Armed { get; set; }

        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed && ++Attempts == 1)
            {
                throw new TimeoutException("Injected before commit; disposal rolls back this attempt.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
