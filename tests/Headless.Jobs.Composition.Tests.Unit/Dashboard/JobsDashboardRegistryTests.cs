// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure.Dashboard;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Dashboard;

public sealed class JobsDashboardRegistryTests : TestBase
{
    private const string _FunctionName = "host-only-dashboard-function";

    [Fact]
    public void should_enumerate_functions_from_the_injected_host_registry()
    {
        var registry = _Registry(_Registration());
        var (repository, _, _, serviceProvider) = _CreateRepository(registry);
        using (serviceProvider)
        {
            repository.GetJobFunctions().Should().ContainSingle().Which.Item1.Should().Be(_FunctionName);
        }
    }

    [Fact]
    public async Task should_dispatch_on_demand_occurrences_with_the_host_registry_delegate_and_priority()
    {
        var registration = _Registration();
        var registry = _Registry(registration);
        var occurrenceId = Guid.Parse("01981f40-29c0-7000-8000-000000000010");
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(occurrenceId);
        var (repository, persistence, dispatcher, serviceProvider) = _CreateRepository(registry, guidGenerator);
        using (serviceProvider)
        {
            var cronJobId = Guid.NewGuid();
            var occurrence = new CronJobOccurrenceEntity<CronJobEntity>
            {
                Id = Guid.NewGuid(),
                CronJobId = cronJobId,
                CronJob = new CronJobEntity { Id = cronJobId, Function = _FunctionName },
            };
            persistence.AcquireImmediateCronOccurrencesAsync(Arg.Any<Guid[]>(), AbortToken).Returns([occurrence]);

            await repository.AddOnDemandCronJobOccurrenceAsync(cronJobId, AbortToken);

            await persistence
                .Received(1)
                .InsertCronJobOccurrencesAsync(
                    Arg.Is<CronJobOccurrenceEntity<CronJobEntity>[]>(items =>
                        items.Length == 1 && items[0].Id == occurrenceId
                    ),
                    AbortToken
                );

            await dispatcher
                .Received(1)
                .DispatchAsync(
                    Arg.Is<JobExecutionState[]>(jobs =>
                        jobs.Length == 1
                        && ReferenceEquals(jobs[0].CachedDelegate, registration.Delegate)
                        && jobs[0].CachedPriority == registration.Priority
                    ),
                    AbortToken
                );
        }
    }

    [Fact]
    public async Task should_validate_requests_with_the_host_registry_request_type()
    {
        var registry = JobFunctionRegistryBuilder.Build(
            [],
            [
                new KeyValuePair<string, (string, Type)>(
                    _FunctionName,
                    (typeof(DashboardRequest).FullName!, typeof(DashboardRequest))
                ),
            ],
            []
        );
        var (repository, persistence, _, serviceProvider) = _CreateRepository(registry);
        using (serviceProvider)
        {
            var jobId = Guid.NewGuid();
            persistence
                .GetTimeJobByIdAsync(jobId, AbortToken)
                .Returns(
                    new TimeJobEntity
                    {
                        Id = jobId,
                        Function = _FunctionName,
                        Request = JobsHelper.CreateJobRequest(
                            new DashboardRequest("host-registry"),
                            JobsRequestSerializationOptions.Default
                        ),
                    }
                );

            var (json, validationState) = await repository.GetJobRequestByIdAsync(jobId, JobType.TimeJob, AbortToken);

            validationState.Should().Be(1);
            json.Should().Contain("host-registry");
        }
    }

    private static JobFunctionRegistration _Registration() =>
        new()
        {
            CronExpression = string.Empty,
            Priority = JobPriority.High,
            Delegate = static (_, _, _) => Task.CompletedTask,
            MaxConcurrency = 0,
        };

    private static JobFunctionRegistry _Registry(JobFunctionRegistration registration) =>
        JobFunctionRegistryBuilder.Build(
            [new KeyValuePair<string, JobFunctionRegistration>(_FunctionName, registration)],
            [],
            []
        );

    private static (
        JobsDashboardRepository<TimeJobEntity, CronJobEntity> Repository,
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> Persistence,
        IJobsDispatcher Dispatcher,
        ServiceProvider ServiceProvider
    ) _CreateRepository(JobFunctionRegistry registry, IGuidGenerator? guidGenerator = null)
    {
        var persistence = Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var dispatcher = Substitute.For<IJobsDispatcher>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var repository = new JobsDashboardRepository<TimeJobEntity, CronJobEntity>(
            new JobsExecutionContext(),
            persistence,
            Substitute.For<IJobsHostScheduler>(),
            Substitute.For<IJobsNotificationHubSender>(),
            new DashboardOptionsBuilder(),
            dispatcher,
            registry,
            TimeProvider.System,
            guidGenerator ?? new SequentialGuidGenerator(SequentialGuidType.Version7),
            serviceProvider,
            JobsRequestSerializationOptions.Default
        );

        return (repository, persistence, dispatcher, serviceProvider);
    }

    private sealed record DashboardRequest(string Value);
}
