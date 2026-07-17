// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<JobsHelperCollection>]
public sealed class JobSchedulerTests : TestBase
{
    private static readonly JobFunctionDescriptor _TypedDescriptor = new(
        "typed",
        typeof(SampleRequest),
        "",
        JobPriority.High,
        2
    );
    private static readonly JobFunctionDescriptor _RequestlessDescriptor = new(
        "requestless",
        null,
        "",
        JobPriority.Normal,
        0
    );

    [Fact]
    public async Task should_enqueue_typed_request_with_supported_options()
    {
        var (scheduler, timeManager, _) = _CreateScheduler();
        var request = new SampleRequest("invoice-42");
        var persistedId = Guid.NewGuid();
        TimeJobEntity? captured = null;
        timeManager
            .AddAsync(Arg.Any<TimeJobEntity>(), AbortToken)
            .Returns(call =>
            {
                captured = call.Arg<TimeJobEntity>();
                captured.Id = persistedId;
                return Task.FromResult(captured);
            });

        var id = await scheduler.EnqueueAsync(
            request,
            new EnqueueOptions
            {
                Description = "Create invoice",
                Retries = 3,
                RetryIntervals = [5, 10],
                OnNodeDeath = NodeDeathPolicy.MarkFailed,
            },
            AbortToken
        );

        id.Should().Be(persistedId);
        captured.Should().NotBeNull();
        captured.Function.Should().Be("typed");
        captured.ExecutionTime.Should().BeNull();
        captured.Description.Should().Be("Create invoice");
        captured.Retries.Should().Be(3);
        captured.RetryIntervals.Should().Equal(5, 10);
        captured.OnNodeDeath.Should().Be(NodeDeathPolicy.MarkFailed);
        JobsHelper.ReadJobRequest<SampleRequest>(captured.Request!).Should().Be(request);
        await timeManager.Received(1).AddAsync(Arg.Any<TimeJobEntity>(), AbortToken);
    }

    [Fact]
    public async Task should_resolve_descriptors_from_the_injected_host_registry()
    {
        var timeManager = Substitute.For<ITimeJobManager<TimeJobEntity>>();
        var cronManager = Substitute.For<ICronJobManager<CronJobEntity>>();
        var persistedId = Guid.NewGuid();
        timeManager
            .AddAsync(Arg.Any<TimeJobEntity>(), AbortToken)
            .Returns(call =>
            {
                var entity = call.Arg<TimeJobEntity>();
                entity.Id = persistedId;
                return entity;
            });
        var registry = JobFunctionRegistryBuilder.Build(
            [],
            [],
            [new KeyValuePair<string, JobFunctionDescriptor>(_TypedDescriptor.FunctionName, _TypedDescriptor)]
        );
        var scheduler = new JobScheduler<TimeJobEntity, CronJobEntity>(
            timeManager,
            cronManager,
            registry,
            Substitute.For<IInternalJobManager>(),
            Substitute.For<IJobsHostScheduler>()
        );

        var id = await scheduler.EnqueueAsync(new SampleRequest("host-registry"), cancellationToken: AbortToken);

        id.Should().Be(persistedId);
        await timeManager
            .Received(1)
            .AddAsync(Arg.Is<TimeJobEntity>(job => job.Function == _TypedDescriptor.FunctionName), AbortToken);
    }

    [Fact]
    public async Task should_accept_the_public_canonical_descriptor_when_the_host_resolves_its_cron_token()
    {
        var canonicalDescriptor = new JobFunctionDescriptor(
            "configured-requestless",
            null,
            "%Jobs:Configured:Cron%",
            JobPriority.Normal,
            0
        );
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["Jobs:Configured:Cron"] = "0 */5 * * * *" }
            )
            .Build();
        var registry = JobFunctionRegistryBuilder.Build(
            [],
            [],
            [new KeyValuePair<string, JobFunctionDescriptor>(canonicalDescriptor.FunctionName, canonicalDescriptor)],
            configuration
        );
        var timeManager = Substitute.For<ITimeJobManager<TimeJobEntity>>();
        var cronManager = Substitute.For<ICronJobManager<CronJobEntity>>();
        timeManager.AddAsync(Arg.Any<TimeJobEntity>(), AbortToken).Returns(call => call.Arg<TimeJobEntity>());
        var scheduler = new JobScheduler<TimeJobEntity, CronJobEntity>(
            timeManager,
            cronManager,
            registry,
            Substitute.For<IInternalJobManager>(),
            Substitute.For<IJobsHostScheduler>()
        );

        await scheduler.EnqueueAsync(canonicalDescriptor, cancellationToken: AbortToken);

        registry.Descriptors[canonicalDescriptor.FunctionName].CronExpression.Should().Be("0 */5 * * * *");
        await timeManager
            .Received(1)
            .AddAsync(Arg.Is<TimeJobEntity>(job => job.Function == canonicalDescriptor.FunctionName), AbortToken);
    }

    [Fact]
    public async Task should_schedule_requestless_delayed_job_without_payload()
    {
        var (scheduler, timeManager, _) = _CreateScheduler();
        var executionTime = new DateTime(2026, 7, 14, 3, 0, 0, DateTimeKind.Utc);
        var persistedId = Guid.NewGuid();
        TimeJobEntity? captured = null;
        timeManager
            .AddAsync(Arg.Any<TimeJobEntity>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<TimeJobEntity>();
                captured.Id = persistedId;
                return Task.FromResult(captured);
            });

        var id = await scheduler.ScheduleAsync(_RequestlessDescriptor, executionTime, cancellationToken: AbortToken);

        id.Should().Be(persistedId);
        captured.Should().NotBeNull();
        captured.Function.Should().Be("requestless");
        captured.Request.Should().BeNull();
        captured.ExecutionTime.Should().Be(executionTime);
    }

    [Fact]
    public async Task should_schedule_typed_delayed_job_with_default_options()
    {
        var (scheduler, timeManager, _) = _CreateScheduler();
        var executionTime = new DateTime(2026, 7, 15, 3, 0, 0, DateTimeKind.Utc);
        TimeJobEntity? captured = null;
        timeManager
            .AddAsync(Arg.Any<TimeJobEntity>(), AbortToken)
            .Returns(call =>
            {
                captured = call.Arg<TimeJobEntity>();
                return Task.FromResult(captured);
            });

        await scheduler.ScheduleAsync(new SampleRequest("delayed"), executionTime, cancellationToken: AbortToken);

        captured.Should().NotBeNull();
        captured!.ExecutionTime.Should().Be(executionTime);
        captured.Description.Should().BeNull();
        captured.Retries.Should().Be(0);
        captured.RetryIntervals.Should().BeNull();
        captured.OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
    }

    [Fact]
    public async Task should_schedule_typed_recurring_definition_and_return_definition_id()
    {
        var (scheduler, _, cronManager) = _CreateScheduler();
        var request = new SampleRequest("daily");
        var persistedId = Guid.NewGuid();
        CronJobEntity? captured = null;
        cronManager
            .AddAsync(Arg.Any<CronJobEntity>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<CronJobEntity>();
                captured.Id = persistedId;
                return Task.FromResult(captured);
            });

        var id = await scheduler.ScheduleRecurringAsync(
            request,
            "0 0 2 * * *",
            new RecurringJobOptions
            {
                Description = "Daily invoice",
                Retries = 2,
                RetryIntervals = [30],
                OnNodeDeath = NodeDeathPolicy.Retry,
            },
            AbortToken
        );

        id.Should().Be(persistedId);
        captured.Should().NotBeNull();
        captured.Function.Should().Be("typed");
        captured.Expression.Should().Be("0 0 2 * * *");
        captured.Description.Should().Be("Daily invoice");
        captured.Retries.Should().Be(2);
        captured.RetryIntervals.Should().Equal(30);
        JobsHelper.ReadJobRequest<SampleRequest>(captured.Request!).Should().Be(request);
    }

    [Fact]
    public async Task should_schedule_requestless_recurring_definition_without_payload()
    {
        var (scheduler, _, cronManager) = _CreateScheduler();
        CronJobEntity? captured = null;
        cronManager
            .AddAsync(Arg.Any<CronJobEntity>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<CronJobEntity>();
                return Task.FromResult(captured);
            });

        await scheduler.ScheduleRecurringAsync(_RequestlessDescriptor, "0 */5 * * * *", cancellationToken: AbortToken);

        captured.Should().NotBeNull();
        captured.Function.Should().Be("requestless");
        captured.Request.Should().BeNull();
    }

    [Fact]
    public async Task should_fail_lookup_before_serialization_or_persistence()
    {
        var (scheduler, timeManager, cronManager) = _CreateScheduler();

        var typed = async () => await scheduler.EnqueueAsync(new UnknownRequest(), cancellationToken: AbortToken);
        var unknownDescriptor = async () =>
            await scheduler.EnqueueAsync(
                new JobFunctionDescriptor("unknown", null, "", JobPriority.Normal, 0),
                cancellationToken: AbortToken
            );
        var staleDescriptor = async () =>
            await scheduler.EnqueueAsync(
                new JobFunctionDescriptor("requestless", null, "0 * * * * *", JobPriority.Normal, 0),
                cancellationToken: AbortToken
            );
        var typedDescriptor = async () => await scheduler.EnqueueAsync(_TypedDescriptor, cancellationToken: AbortToken);

        (await typed.Should().ThrowAsync<JobFunctionNotFoundException>())
            .Which.RequestType.Should()
            .Be<UnknownRequest>();
        (await unknownDescriptor.Should().ThrowAsync<JobFunctionNotFoundException>())
            .Which.FunctionName.Should()
            .Be("unknown");
        await staleDescriptor.Should().ThrowAsync<JobFunctionNotFoundException>();
        await typedDescriptor.Should().ThrowAsync<ArgumentException>();
        await timeManager.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
        await cronManager.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_not_persist_when_request_serialization_fails()
    {
        var timeManager = Substitute.For<ITimeJobManager<TimeJobEntity>>();
        var cronManager = Substitute.For<ICronJobManager<CronJobEntity>>();
        var descriptor = new JobFunctionDescriptor(
            "unsupported",
            typeof(UnsupportedRequest),
            "",
            JobPriority.Normal,
            0
        );
        var scheduler = new JobScheduler<TimeJobEntity, CronJobEntity>(
            timeManager,
            cronManager,
            requestType => requestType == typeof(UnsupportedRequest) ? descriptor : null,
            _ => null,
            Substitute.For<IInternalJobManager>(),
            Substitute.For<IJobsHostScheduler>()
        );

        var act = async () =>
            await scheduler.EnqueueAsync(new UnsupportedRequest(typeof(string)), cancellationToken: AbortToken);

        await act.Should().ThrowAsync<NotSupportedException>();
        await timeManager.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
        await cronManager.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_use_configured_json_options_and_gzip_serialization()
    {
        var previousOptions = JobsHelper.RequestJsonSerializerOptions;
        var previousCompression = JobsHelper.UseGZipCompression;

        try
        {
            JobsHelper.RequestJsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            JobsHelper.UseGZipCompression = true;
            var (scheduler, timeManager, _) = _CreateScheduler();
            TimeJobEntity? captured = null;
            timeManager
                .AddAsync(Arg.Any<TimeJobEntity>(), AbortToken)
                .Returns(call =>
                {
                    captured = call.Arg<TimeJobEntity>();
                    return Task.FromResult(captured);
                });

            var request = new SampleRequest("compressed");
            await scheduler.EnqueueAsync(request, cancellationToken: AbortToken);

            captured.Should().NotBeNull();
            JobsHelper.ReadJobRequestAsString(captured!.Request!).Should().Contain("\"value\"");
            JobsHelper.ReadJobRequest<SampleRequest>(captured.Request!).Should().Be(request);
        }
        finally
        {
            JobsHelper.RequestJsonSerializerOptions = previousOptions;
            JobsHelper.UseGZipCompression = previousCompression;
        }
    }

    [Fact]
    public async Task cancel_async_delegates_to_the_internal_durable_cancellation_operation()
    {
        var timeManager = Substitute.For<ITimeJobManager<TimeJobEntity>>();
        var cronManager = Substitute.For<ICronJobManager<CronJobEntity>>();
        var internalManager = Substitute.For<IInternalJobManager>();
        var hostScheduler = Substitute.For<IJobsHostScheduler>();
        var jobId = Guid.NewGuid();
        internalManager.RequestTimeJobCancellationAsync(jobId, AbortToken).Returns(true);
        var scheduler = new JobScheduler<TimeJobEntity, CronJobEntity>(
            timeManager,
            cronManager,
            JobFunctionRegistryBuilder.Build([], [], []),
            internalManager,
            hostScheduler
        );

        (await scheduler.CancelAsync(jobId, AbortToken)).Should().BeTrue();

        await internalManager.Received(1).RequestTimeJobCancellationAsync(jobId, AbortToken);
        hostScheduler.Received(1).Restart();
    }

    [Fact]
    public async Task cancel_async_does_not_restart_the_host_when_the_transition_is_rejected()
    {
        var timeManager = Substitute.For<ITimeJobManager<TimeJobEntity>>();
        var cronManager = Substitute.For<ICronJobManager<CronJobEntity>>();
        var internalManager = Substitute.For<IInternalJobManager>();
        var hostScheduler = Substitute.For<IJobsHostScheduler>();
        var jobId = Guid.NewGuid();
        internalManager.RequestTimeJobCancellationAsync(jobId, AbortToken).Returns(false);
        var scheduler = new JobScheduler<TimeJobEntity, CronJobEntity>(
            timeManager,
            cronManager,
            JobFunctionRegistryBuilder.Build([], [], []),
            internalManager,
            hostScheduler
        );

        (await scheduler.CancelAsync(jobId, AbortToken)).Should().BeFalse();

        hostScheduler.DidNotReceive().Restart();
    }

    [Fact]
    public void should_expose_only_the_job_id_cancellation_method_and_six_scheduling_overloads()
    {
        var methods = typeof(IJobScheduler).GetMethods(BindingFlags.Instance | BindingFlags.Public);

        methods.Should().HaveCount(7);
        methods.Count(method => method.ReturnType == typeof(Task<Guid>)).Should().Be(6);
        var cancellation = methods.Single(method =>
            string.Equals(method.Name, nameof(IJobScheduler.CancelAsync), StringComparison.Ordinal)
        );
        cancellation.ReturnType.Should().Be<Task<bool>>();
        cancellation
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Equal(typeof(Guid), typeof(CancellationToken));
        cancellation.GetParameters()[^1].HasDefaultValue.Should().BeTrue();
        _AssertOverload(methods, nameof(IJobScheduler.EnqueueAsync), true, typeof(EnqueueOptions));
        _AssertOverload(methods, nameof(IJobScheduler.EnqueueAsync), false, typeof(EnqueueOptions));
        _AssertOverload(methods, nameof(IJobScheduler.ScheduleAsync), true, typeof(DateTime), typeof(EnqueueOptions));
        _AssertOverload(methods, nameof(IJobScheduler.ScheduleAsync), false, typeof(DateTime), typeof(EnqueueOptions));
        _AssertOverload(
            methods,
            nameof(IJobScheduler.ScheduleRecurringAsync),
            true,
            typeof(string),
            typeof(RecurringJobOptions)
        );
        _AssertOverload(
            methods,
            nameof(IJobScheduler.ScheduleRecurringAsync),
            false,
            typeof(string),
            typeof(RecurringJobOptions)
        );
        typeof(EnqueueOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Should()
            .BeEquivalentTo(
                nameof(EnqueueOptions.Description),
                nameof(EnqueueOptions.Retries),
                nameof(EnqueueOptions.RetryIntervals),
                nameof(EnqueueOptions.OnNodeDeath)
            );
        typeof(RecurringJobOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Should()
            .BeEquivalentTo(
                nameof(RecurringJobOptions.Description),
                nameof(RecurringJobOptions.Retries),
                nameof(RecurringJobOptions.RetryIntervals),
                nameof(RecurringJobOptions.OnNodeDeath)
            );
    }

    [Fact]
    public void should_register_the_facade_for_the_configured_entity_pair()
    {
        var defaultServices = new ServiceCollection();
        defaultServices.AddLogging();
        defaultServices.AddHeadlessJobs(options => options.DisableBackgroundServices());

        using var defaultProvider = defaultServices.BuildServiceProvider();
        defaultProvider
            .GetRequiredService<IJobScheduler>()
            .Should()
            .BeOfType<JobScheduler<TimeJobEntity, CronJobEntity>>();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessJobs<CustomTimeJob, CustomCronJob>(options => options.DisableBackgroundServices());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IJobScheduler>().Should().BeOfType<JobScheduler<CustomTimeJob, CustomCronJob>>();
    }

    private static (
        IJobScheduler Scheduler,
        ITimeJobManager<TimeJobEntity> TimeManager,
        ICronJobManager<CronJobEntity> CronManager
    ) _CreateScheduler()
    {
        var timeManager = Substitute.For<ITimeJobManager<TimeJobEntity>>();
        var cronManager = Substitute.For<ICronJobManager<CronJobEntity>>();
        var scheduler = new JobScheduler<TimeJobEntity, CronJobEntity>(
            timeManager,
            cronManager,
            requestType => requestType == typeof(SampleRequest) ? _TypedDescriptor : null,
            functionName =>
                string.Equals(functionName, _RequestlessDescriptor.FunctionName, StringComparison.Ordinal)
                    ? _RequestlessDescriptor
                    : null,
            Substitute.For<IInternalJobManager>(),
            Substitute.For<IJobsHostScheduler>()
        );

        return (scheduler, timeManager, cronManager);
    }

    private static void _AssertOverload(
        MethodInfo[] methods,
        string name,
        bool generic,
        params Type[] middleParameterTypes
    )
    {
        var method = methods.Single(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal)
            && candidate.IsGenericMethodDefinition == generic
        );
        var parameters = method.GetParameters();

        method.GetGenericArguments().Should().HaveCount(generic ? 1 : 0);
        if (generic)
        {
            parameters[0].ParameterType.IsGenericParameter.Should().BeTrue();
        }
        else
        {
            parameters[0].ParameterType.Should().Be<JobFunctionDescriptor>();
        }

        parameters[1..^1].Select(parameter => parameter.ParameterType).Should().Equal(middleParameterTypes);
        parameters[^2].HasDefaultValue.Should().BeTrue();
        parameters[^1].ParameterType.Should().Be<CancellationToken>();
        parameters[^1].HasDefaultValue.Should().BeTrue();
    }

    private sealed record SampleRequest(string Value);

    private sealed record UnknownRequest;

    private sealed record UnsupportedRequest(Type Value);

    private sealed class CustomTimeJob : TimeJobEntity<CustomTimeJob>;

    private sealed class CustomCronJob : CronJobEntity;
}

[CollectionDefinition(DisableParallelization = true)]
public sealed class JobsHelperCollection;
