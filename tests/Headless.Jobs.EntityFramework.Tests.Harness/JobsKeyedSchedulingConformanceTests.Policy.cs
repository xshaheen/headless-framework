// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
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
}
