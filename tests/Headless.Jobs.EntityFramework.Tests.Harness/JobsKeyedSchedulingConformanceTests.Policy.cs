// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public abstract partial class JobsKeyedSchedulingConformanceTests<TFixture>
{
    public virtual async Task independently_configured_schedulers_preserve_policy_across_restart(string source)
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        var request = new CoordinatedFacadeRequest(Guid.NewGuid(), "invoice-42");
        using (
            var first = Fixture.BuildHost(
                "policy-a",
                configureJobs: options =>
                    JobsKeyedPolicyScenarios.Configure<CoordinatedFacadeRequest>(options, 0, source)
            )
        )
        using (
            var second = Fixture.BuildHost(
                "policy-b",
                configureJobs: options =>
                    JobsKeyedPolicyScenarios.Configure<CoordinatedFacadeRequest>(options, 1, source)
            )
        )
        {
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(first, AbortToken);
            await JobsKeyedPolicyScenarios.RunAsync(
                first.Services.GetRequiredService<IJobScheduler>(),
                second.Services.GetRequiredService<IJobScheduler>(),
                first.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>(),
                request,
                source,
                AbortToken
            );
        }

        using var restarted = Fixture.BuildHost(
            "policy-restarted",
            configureJobs: options => JobsKeyedPolicyScenarios.Configure<CoordinatedFacadeRequest>(options, 0, source)
        );
        var result = await restarted
            .Services.GetRequiredService<IJobScheduler>()
            .ScheduleKeyedAsync(
                new JobKey($"{source}-winner-0"),
                request,
                new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                string.Equals(source, "call", StringComparison.Ordinal) ? JobsKeyedPolicyScenarios.Policy(0) : null,
                AbortToken
            );
        result.Disposition.Should().Be(JobScheduleDisposition.Existing);
        result.Generation.Should().Be(2);
        var row = await restarted
            .Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>()
            .GetTimeJobByIdAsync(result.RunId!.Value, AbortToken);
        JobsKeyedPolicyScenarios.AssertPolicy(row!, JobsKeyedPolicyScenarios.Policy(1));
    }

    public virtual async Task retained_v1_observation_preserves_the_stored_generation()
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        using var host = Fixture.BuildHost("legacy-policy");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, AbortToken);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var key = new JobKey("legacy-policy");
        var created = await store.ScheduleKeyedTimeJobAsync(
            key,
            JobsKeyedSchedulingScenarios.Candidate(),
            cancellationToken: AbortToken
        );
        await using (
            var context = await host
                .Services.GetRequiredService<IDbContextFactory<JobsDbContext>>()
                .CreateDbContextAsync(AbortToken)
        )
        {
            // The fixed v1 golden hash represents this candidate before retry execution moved its due time.
            await context
                .Set<TimeJobEntity>()
                .Where(row => row.Id == created.RunId)
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(row => row.FingerprintAlgorithm, "v1")
                            .SetProperty(
                                row => row.IntentFingerprint,
                                "caa4a313cae19b0fe80623b8f440a4c20bd7749c2384aa61e9019770fdcca86f"
                            )
                            .SetProperty(row => row.Status, JobStatus.Succeeded)
                            .SetProperty(row => row.ExecutionTime, new DateTime(2030, 1, 1, 1, 0, 0, DateTimeKind.Utc)),
                    AbortToken
                );
        }
        var before = await store.GetTimeJobByIdAsync(created.RunId!.Value, AbortToken);
        var candidate = JobsKeyedSchedulingScenarios.Candidate();
        candidate.Retries = 5;
        candidate.RetryIntervals = [2, 4];
        candidate.OnNodeDeath = NodeDeathPolicy.Skip;
        var result = await store.ScheduleKeyedTimeJobAsync(key, candidate, cancellationToken: AbortToken);
        result.Disposition.Should().Be(JobScheduleDisposition.Existing);
        result.State.Should().Be(JobStatus.Succeeded);
        (await store.GetTimeJobByIdAsync(created.RunId.Value, AbortToken)).Should().BeEquivalentTo(before);
        candidate.Request = [9];
        (await store.ScheduleKeyedTimeJobAsync(key, candidate, cancellationToken: AbortToken))
            .Disposition.Should()
            .Be(JobScheduleDisposition.Conflict);
    }
}
