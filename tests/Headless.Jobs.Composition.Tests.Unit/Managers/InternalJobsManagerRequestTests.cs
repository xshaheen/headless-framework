// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Jobs;
using Headless.Jobs.Managers;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Managers;

/// <summary>
/// The generated job delegate reads its payload on every attempt. The contract check runs against the executing
/// context, so the read is one payload query and never a load of the whole row.
/// </summary>
public sealed class InternalJobsManagerRequestTests : TestBase
{
    public sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    public sealed class FakeCronJob : CronJobEntity;

    private sealed record SampleRequest(int Value);

    private const string _Function = "typed-function";

    private readonly IJobPersistenceProvider<FakeTimeJob, FakeCronJob> _provider = Substitute.For<
        IJobPersistenceProvider<FakeTimeJob, FakeCronJob>
    >();

    [Fact]
    public async Task should_read_only_the_time_job_payload_when_the_context_contract_matches()
    {
        var context = _Context(JobType.TimeJob, "v2");
        _provider
            .GetTimeJobRequestAsync(context.Id, AbortToken)
            .Returns(JobsHelper.CreateJobRequest(new SampleRequest(7), JobsRequestSerializationOptions.Default));
        var manager = _CreateManager(registeredVersion: "v2");

        var request = await manager.GetRequestAsync<SampleRequest>(context, AbortToken);

        request.Should().Be(new SampleRequest(7));
        await _provider.DidNotReceiveWithAnyArgs().GetTimeJobByIdAsync(default, AbortToken);
    }

    [Fact]
    public async Task should_read_only_the_occurrence_payload_when_the_context_contract_matches()
    {
        var context = _Context(JobType.CronJobOccurrence, "v2");
        _provider
            .GetCronJobOccurrenceRequestAsync(context.Id, AbortToken)
            .Returns(JobsHelper.CreateJobRequest(new SampleRequest(3), JobsRequestSerializationOptions.Default));
        var manager = _CreateManager(registeredVersion: "v2");

        var request = await manager.GetRequestAsync<SampleRequest>(context, AbortToken);

        request.Should().Be(new SampleRequest(3));
        await _provider.DidNotReceiveWithAnyArgs().GetAllCronJobOccurrencesAsync(default, AbortToken);
    }

    [Fact]
    public async Task should_reject_the_payload_without_reading_it_when_the_context_version_is_not_registered()
    {
        var context = _Context(JobType.TimeJob, "v1");
        var manager = _CreateManager(registeredVersion: "v2");

        var act = () => manager.GetRequestAsync<SampleRequest>(context, AbortToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("version 'v1'");
        await _provider.DidNotReceiveWithAnyArgs().GetTimeJobRequestAsync(default, AbortToken);
        await _provider.DidNotReceiveWithAnyArgs().GetTimeJobByIdAsync(default, AbortToken);
    }

    private static JobContext _Context(JobType type, string contractVersion)
    {
        return new JobContext
        {
            Id = Guid.NewGuid(),
            Type = type,
            FunctionName = _Function,
            ContractVersion = contractVersion,
        };
    }

    private InternalJobsManager<FakeTimeJob, FakeCronJob> _CreateManager(string registeredVersion)
    {
        var descriptor = new JobFunctionDescriptor(
            _Function,
            typeof(SampleRequest),
            "",
            JobPriority.Normal,
            0,
            registeredVersion
        );
        var registry = JobFunctionRegistryBuilder.Build(
            [],
            [],
            [new KeyValuePair<string, JobFunctionDescriptor>(_Function, descriptor)]
        );
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(JobFunctionRegistry)).Returns(registry);

        return new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            _provider,
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(),
            Substitute.For<IJobsNotificationHubSender>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            Substitute.For<IGuidGenerator>(),
            services,
            new SchedulerOptionsBuilder()
        );
    }
}
