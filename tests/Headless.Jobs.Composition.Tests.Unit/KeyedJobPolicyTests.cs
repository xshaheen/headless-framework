// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
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
