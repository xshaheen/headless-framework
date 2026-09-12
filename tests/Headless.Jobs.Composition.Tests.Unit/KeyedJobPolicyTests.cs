// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<JobsHelperCollection>]
public sealed class KeyedJobPolicyTests : TestBase
{
    [Theory]
    [InlineData("host")]
    [InlineData("function")]
    [InlineData("call")]
    public async Task independently_configured_schedulers_observe_the_winning_policy(string source)
    {
        await using var first = _Host(0, source);
        var store = first.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        await using var second = _Host(1, source, store);
        await JobsKeyedPolicyScenarios.RunAsync(
            first.GetRequiredService<IJobScheduler>(),
            second.GetRequiredService<IJobScheduler>(),
            store,
            new Request("invoice-42"),
            source,
            AbortToken
        );
    }

    [Fact]
    public async Task retained_v1_observation_preserves_the_stored_generation()
    {
        await using var host = _Host(0, "host");
        var store = host.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var key = new JobKey("legacy");
        var result = await store.ScheduleKeyedTimeJobAsync(
            key,
            JobsKeyedSchedulingScenarios.Candidate(),
            cancellationToken: AbortToken
        );
        // Seed the pre-upgrade representation directly; ordinary writes deliberately prohibit keyed mutation.
        var rows =
            (ConcurrentDictionary<Guid, TimeJobEntity>)
                typeof(JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>)
                    .GetField("_timeJobs", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
                    .GetValue(store)!;
        var retained = rows[result.RunId!.Value];
        retained.FingerprintAlgorithm = "v1";
        retained.IntentFingerprint = "caa4a313cae19b0fe80623b8f440a4c20bd7749c2384aa61e9019770fdcca86f";
        retained.Status = JobStatus.Succeeded;
        retained.ExecutionTime = retained.ExecutionTime!.Value.AddHours(1);
        var before = retained.Clone();
        var candidate = JobsKeyedSchedulingScenarios.Candidate();
        candidate.Retries = 5;
        candidate.RetryIntervals = [2, 4];
        candidate.OnNodeDeath = NodeDeathPolicy.Skip;
        var observed = await store.ScheduleKeyedTimeJobAsync(key, candidate, cancellationToken: AbortToken);
        observed.Disposition.Should().Be(JobScheduleDisposition.Existing);
        observed.State.Should().Be(JobStatus.Succeeded);
        (await store.GetTimeJobByIdAsync(result.RunId.Value, AbortToken)).Should().BeEquivalentTo(before);
        candidate.Request = [9];
        (await store.ScheduleKeyedTimeJobAsync(key, candidate, cancellationToken: AbortToken))
            .Disposition.Should()
            .Be(JobScheduleDisposition.Conflict);
    }

    private static ServiceProvider _Host(
        int host,
        string source,
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity>? store = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            JobsKeyedPolicyScenarios.Configure<Request>(options, host, source);
        });
        var descriptor = new JobFunctionDescriptor("policy-host", typeof(Request), "", JobPriority.Normal, 0);
        services.AddSingleton(
            JobFunctionRegistryBuilder.Build(
                [
                    new KeyValuePair<string, JobFunctionRegistration>(
                        descriptor.FunctionName,
                        new()
                        {
                            CronExpression = "",
                            Priority = JobPriority.Normal,
                            MaxConcurrency = 0,
                            Delegate = (_, _, _) => Task.CompletedTask,
                        }
                    ),
                ],
                [],
                [new KeyValuePair<string, JobFunctionDescriptor>(descriptor.FunctionName, descriptor)]
            )
        );
        if (store is not null)
        {
            services.AddSingleton(store);
        }
        return services.BuildServiceProvider();
    }

    public sealed record Request(string Value);
}
