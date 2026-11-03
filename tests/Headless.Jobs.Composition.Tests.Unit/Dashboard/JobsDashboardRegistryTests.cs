// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
using Headless;
using Headless.Jobs;
using Headless.Jobs.Infrastructure.Dashboard;
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
    public void should_generate_a_representative_request_example_from_the_host_registry_type()
    {
        var registry = JobFunctionRegistryBuilder.Build(
            [new KeyValuePair<string, JobFunctionRegistration>(_FunctionName, _Registration())],
            [
                new KeyValuePair<string, (string, Type)>(
                    _FunctionName,
                    (typeof(ExampleRequest).FullName!, typeof(ExampleRequest))
                ),
            ],
            []
        );
        var (repository, _, _, serviceProvider) = _CreateRepository(registry);
        using (serviceProvider)
        {
            var function = repository.GetJobFunctions().Should().ContainSingle().Which;

            function.Item2.Item1.Should().Be(typeof(ExampleRequest).FullName);
            function.Item2.Item2.Should().Contain("\"Name\": \"string\"");
            function.Item2.Item2.Should().Contain("\"Retries\": 123");
            function.Item2.Item2.Should().Contain("\"Tags\": [");
            function.Item2.Item3.Should().Be(JobPriority.High);
        }
    }

    [Fact]
    public void should_generate_the_request_example_with_the_configured_request_json_options()
    {
        // A host that names request properties in camelCase deserializes job payloads with the same options; an
        // example written in PascalCase would bind no property and the job would run on a defaulted request.
        var serialization = new JobsRequestSerializationOptions
        {
            SerializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
        };
        var registry = JobFunctionRegistryBuilder.Build(
            [new KeyValuePair<string, JobFunctionRegistration>(_FunctionName, _Registration())],
            [
                new KeyValuePair<string, (string, Type)>(
                    _FunctionName,
                    (typeof(ExampleRequest).FullName!, typeof(ExampleRequest))
                ),
            ],
            []
        );
        var (repository, _, _, serviceProvider) = _CreateRepository(registry, serialization: serialization);
        using (serviceProvider)
        {
            var example = repository.GetJobFunctions().Should().ContainSingle().Which.Item2.Item2;

            example.Should().Contain("\"name\": \"string\"");
            var request = JsonSerializer.Deserialize<ExampleRequest>(example, serialization.SerializerOptions);
            request.Should().NotBeNull();
            request!.Name.Should().Be("string");
            request.Retries.Should().Be(123);
            request.Tags.Should().Equal("string");
        }
    }

    [Fact]
    public void should_return_an_empty_request_example_when_the_registered_type_cannot_be_constructed()
    {
        var registry = JobFunctionRegistryBuilder.Build(
            [new KeyValuePair<string, JobFunctionRegistration>(_FunctionName, _Registration())],
            [
                new KeyValuePair<string, (string, Type)>(
                    _FunctionName,
                    (typeof(AbstractRequest).FullName!, typeof(AbstractRequest))
                ),
            ],
            []
        );
        var (repository, _, _, serviceProvider) = _CreateRepository(registry);
        using (serviceProvider)
        {
            var function = repository.GetJobFunctions().Should().ContainSingle().Which;

            function.Item2.Item1.Should().Be(typeof(AbstractRequest).FullName);
            function.Item2.Item2.Should().BeEmpty();
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
                Function = _FunctionName,
                ContractVersion = "1",
                CorrelationId = "business-root",
                CausationId = "direct-cause",
                CronJob = new CronJobEntity { Id = cronJobId, Function = "edited-parent" },
            };
            persistence.AcquireImmediateCronOccurrencesAsync(Arg.Any<Guid[]>(), AbortToken).Returns([occurrence]);

            await repository.AddOnDemandCronJobOccurrenceAsync(cronJobId, AbortToken);

            await persistence
                .Received(1)
                .InsertRunNowCronJobOccurrenceAsync(
                    Arg.Is<CronJobOccurrenceEntity<CronJobEntity>>(item => item.Id == occurrenceId),
                    AbortToken
                );

            await dispatcher
                .Received(1)
                .DispatchAsync(
                    Arg.Is<JobExecutionState[]>(jobs =>
                        jobs.Length == 1
                        && jobs[0].FunctionName == _FunctionName
                        && jobs[0].ContractVersion == "1"
                        && jobs[0].CorrelationId == "business-root"
                        && jobs[0].CausationId == "direct-cause"
                        && ReferenceEquals(jobs[0].CachedDelegate, registration.Delegate)
                        && jobs[0].CachedPriority == registration.Priority
                    ),
                    AbortToken
                );
        }
    }

    [Fact]
    public async Task unsupported_stored_version_is_displayed_before_malformed_request_deserialization()
    {
        var descriptor = new JobFunctionDescriptor(
            _FunctionName,
            typeof(DashboardRequest),
            "",
            JobPriority.Normal,
            0,
            "v2"
        );
        var registry = JobFunctionRegistryBuilder.Build(
            [],
            [],
            [new KeyValuePair<string, JobFunctionDescriptor>(_FunctionName, descriptor)]
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
                        ContractVersion = "v1",
                        Request = [255, 254],
                        CorrelationId = "business-root",
                        CausationId = "direct-cause",
                        RetryCount = 7,
                    }
                );

            var (diagnostic, validationState) = await repository.GetJobRequestByIdAsync(
                jobId,
                JobType.TimeJob,
                AbortToken
            );

            validationState.Should().Be(2);
            diagnostic.Should().Contain("version 'v1'").And.Contain("registers 'v2'").And.Contain("not deserialized");
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

    [Fact]
    public async Task should_validate_stored_requests_with_the_request_json_options_the_job_binds_with()
    {
        // The stored payload is written, and bound by the job, with the request options; a converter registered only
        // there must count when the dashboard reports whether the payload matches the request type.
        var serialization = new JobsRequestSerializationOptions
        {
            SerializerOptions = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } },
        };
        var registry = JobFunctionRegistryBuilder.Build(
            [],
            [
                new KeyValuePair<string, (string, Type)>(
                    _FunctionName,
                    (typeof(ColoredRequest).FullName!, typeof(ColoredRequest))
                ),
            ],
            []
        );
        var (repository, persistence, _, serviceProvider) = _CreateRepository(registry, serialization: serialization);
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
                        Request = JobsHelper.CreateJobRequest(new ColoredRequest(RequestColor.Blue), serialization),
                    }
                );

            var (json, validationState) = await repository.GetJobRequestByIdAsync(jobId, JobType.TimeJob, AbortToken);

            json.Should().Contain("\"Blue\"");
            validationState.Should().Be(1);
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
    ) _CreateRepository(
        JobFunctionRegistry registry,
        IGuidGenerator? guidGenerator = null,
        JobsRequestSerializationOptions? serialization = null
    )
    {
        var persistence = Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var dispatcher = Substitute.For<IJobsDispatcher>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var repository = new JobsDashboardRepository<TimeJobEntity, CronJobEntity>(
            new JobsExecutionContext(),
            persistence,
            Substitute.For<IJobsHostScheduler>(),
            Substitute.For<IJobsNotificationHubSender>(),
            dispatcher,
            registry,
            TimeProvider.System,
            guidGenerator ?? new SequentialGuidGenerator(SequentialGuidType.Version7),
            serviceProvider,
            serialization ?? JobsRequestSerializationOptions.Default
        );

        return (repository, persistence, dispatcher, serviceProvider);
    }

    private sealed record DashboardRequest(string Value);

    private enum RequestColor
    {
        Red,
        Blue,
    }

    private sealed record ColoredRequest(RequestColor Color);

    private sealed class ExampleRequest
    {
        public string Name { get; set; } = string.Empty;

        public int? Retries { get; set; }

        public List<string> Tags { get; set; } = [];
    }

    private abstract class AbstractRequest;
}
