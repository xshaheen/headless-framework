// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

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
            options.DisableBackgroundServices().AddModule<PolicyHostJobsModule>();
            JobsKeyedPolicyScenarios.Configure<Request>(options, host, source, _FunctionName);
        });
        if (store is not null)
        {
            services.AddSingleton(store);
        }

        return services.BuildServiceProvider();
    }

    public sealed record Request(string Value);

    private const string _FunctionName = "policy-host";

    // Registered through a module so the host's catalog applies its tuning and default failure policy.
    public sealed class PolicyHostJobsModule : IJobsModule
    {
        private PolicyHostJobsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog)
        {
            catalog.AddFunctions(
                new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
                {
                    [_FunctionName] = new()
                    {
                        CronExpression = "",
                        Priority = JobPriority.Normal,
                        MaxConcurrency = 0,
                        Delegate = static (_, _, _) => Task.CompletedTask,
                    },
                }
            );
            catalog.AddRequestTypes(
                new Dictionary<string, (string, Type)>(StringComparer.Ordinal)
                {
                    [_FunctionName] = (typeof(Request).FullName!, typeof(Request)),
                }
            );
            catalog.AddDescriptors(
                new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
                {
                    [_FunctionName] = new(_FunctionName, typeof(Request), "", JobPriority.Normal, 0),
                }
            );
        }
    }
}
