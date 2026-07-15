// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure.Dashboard;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Dashboard;

public sealed class JobsDashboardRegistryTests
{
    [Fact]
    public void should_enumerate_functions_from_the_injected_host_registry()
    {
        const string functionName = "host-only-dashboard-function";
        var registration = new JobFunctionRegistration
        {
            CronExpression = string.Empty,
            Priority = JobPriority.High,
            Delegate = static (_, _, _) => Task.CompletedTask,
            MaxConcurrency = 0,
        };
        var registry = JobFunctionRegistryBuilder.Build(
            [new KeyValuePair<string, JobFunctionRegistration>(functionName, registration)],
            [],
            []
        );
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var repository = new JobsDashboardRepository<TimeJobEntity, CronJobEntity>(
            new JobsExecutionContext(),
            Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>(),
            Substitute.For<IJobsHostScheduler>(),
            Substitute.For<IJobsNotificationHubSender>(),
            new DashboardOptionsBuilder(),
            Substitute.For<IJobsDispatcher>(),
            registry,
            TimeProvider.System,
            serviceProvider
        );

        repository.GetJobFunctions().Should().ContainSingle().Which.Item1.Should().Be(functionName);
    }
}
