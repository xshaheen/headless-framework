// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.Coordination;
using Headless.Jobs.Dispatcher;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Internal;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry.Metrics;

namespace Tests.Instrumentation;

/// <summary>
/// The execution, retry, and queue-lag metrics a run emits, observed through a <see cref="MeterListener" /> on the
/// Jobs meter, and the per-host bound on their function tag.
/// </summary>
[Collection<JobsInstrumentationCollection>]
public sealed class JobsMetricsTests : TestBase
{
    // Instrument and attribute names are written out here, not read from the code under test, so a rename fails.
    private const string _Duration = "headless.jobs.execution.duration";
    private const string _Outcomes = "headless.jobs.execution.outcomes";
    private const string _Retries = "headless.jobs.execution.retries";
    private const string _QueueLag = "headless.jobs.queue.lag";
    private const string _Skipped = "headless.jobs.cron.occurrences.skipped";
    private const string _FunctionTag = "headless.jobs.function";
    private const string _OutcomeTag = "headless.jobs.outcome";
    private const string _ErrorTypeTag = "error.type";
    private const string _RetryKindTag = "headless.jobs.retry.kind";
    private const string _TriggerTag = "headless.jobs.trigger";

    private static readonly DateTimeOffset _Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_record_one_duration_sample_and_a_succeeded_outcome_when_the_handler_succeeds()
    {
        // given
        const string function = "metrics.succeeds";
        using var recorder = new MetricRecorder();
        var handler = _Handler(_HealthyManager(), _Registry(function));

        // when
        await handler.ExecuteTaskAsync(
            _Job(function, static (_, _, _) => Task.CompletedTask),
            isDue: false,
            AbortToken
        );

        // then
        var duration = recorder.Of(_Duration).Should().ContainSingle().Subject;
        duration.Tags.Should().Contain(_FunctionTag, function).And.Contain(_OutcomeTag, "succeeded");
        duration.Tags.Should().NotContainKey(_ErrorTypeTag);
        duration.Value.Should().BeGreaterThanOrEqualTo(0);
        var outcome = recorder.Of(_Outcomes).Should().ContainSingle().Subject;
        outcome.Value.Should().Be(1);
        outcome.Tags.Should().Contain(_FunctionTag, function).And.Contain(_OutcomeTag, "succeeded");
    }

    [Fact]
    public async Task should_record_a_failed_outcome_with_the_exception_type_when_the_handler_throws()
    {
        // given
        const string function = "metrics.throws";
        using var recorder = new MetricRecorder();
        var handler = _Handler(_HealthyManager(), _Registry(function));
        var job = _Job(function, static (_, _, _) => throw new InvalidOperationException("the job fails"));

        // when
        await handler.ExecuteTaskAsync(job, isDue: false, AbortToken);

        // then
        var duration = recorder.Of(_Duration).Should().ContainSingle().Subject;
        duration
            .Tags.Should()
            .Contain(_OutcomeTag, "failed")
            .And.Contain(_ErrorTypeTag, "System.InvalidOperationException");
        recorder
            .Of(_Outcomes)
            .Should()
            .ContainSingle()
            .Which.Tags.Should()
            .Contain(_OutcomeTag, "failed")
            .And.Contain(_ErrorTypeTag, "System.InvalidOperationException");
    }

    [Fact]
    public async Task should_record_a_skipped_outcome_and_no_duration_for_a_chain_child_that_never_ran()
    {
        // given: a failing parent whose child runs only on success
        const string parentFunction = "metrics.parent";
        const string childFunction = "metrics.child";
        using var recorder = new MetricRecorder();
        var handler = _Handler(_HealthyManager(), _Registry(parentFunction, childFunction));
        var parent = _Job(parentFunction, static (_, _, _) => throw new InvalidOperationException("parent fails"));
        var childRan = false;
        parent.TimeJobChildren.Add(
            _Job(
                childFunction,
                (_, _, _) =>
                {
                    childRan = true;
                    return Task.CompletedTask;
                }
            )
        );

        // when
        await handler.ExecuteTaskAsync(parent, isDue: false, AbortToken);

        // then
        childRan.Should().BeFalse();
        recorder
            .Of(_Outcomes)
            .Where(x => Equals(x.Tags[_FunctionTag], childFunction))
            .Should()
            .ContainSingle()
            .Which.Tags.Should()
            .Contain(_OutcomeTag, "skipped");
        recorder.Of(_Duration).Should().NotContain(x => Equals(x.Tags[_FunctionTag], childFunction));
    }

    [Fact]
    public async Task should_record_a_failed_outcome_and_no_duration_when_recovery_already_spent_the_budget()
    {
        // given: crash-recovery reclaims consumed more attempts than the budget allows
        const string function = "metrics.exhausted";
        using var recorder = new MetricRecorder();
        var handler = _Handler(_HealthyManager(), _Registry(function));
        var job = _Job(function, static (_, _, _) => Task.CompletedTask, retries: 1, retryCount: 2);

        // when
        await handler.ExecuteTaskAsync(job, isDue: false, AbortToken);

        // then
        recorder.Of(_Duration).Should().BeEmpty("the handler was never invoked");
        recorder.Of(_Outcomes).Should().ContainSingle().Which.Tags.Should().Contain(_OutcomeTag, "failed");
        recorder.Of(_Retries).Should().BeEmpty("a run that never invoked the handler retried nothing");
    }

    [Fact]
    public async Task should_count_an_in_process_retry_when_a_failure_schedules_another_attempt()
    {
        // given: the first attempt fails, the second succeeds
        const string function = "metrics.retries";
        using var recorder = new MetricRecorder();
        var handler = _Handler(_HealthyManager(), _Registry(function));
        var attempts = 0;
        var job = _Job(
            function,
            (_, _, _) => ++attempts == 1 ? throw new InvalidOperationException("transient") : Task.CompletedTask,
            retries: 2
        );

        // when
        await handler.ExecuteTaskAsync(job, isDue: false, AbortToken);

        // then
        attempts.Should().Be(2);
        var retry = recorder.Of(_Retries).Should().ContainSingle().Subject;
        retry.Value.Should().Be(1);
        retry.Tags.Should().Contain(_RetryKindTag, "in_process");
        recorder.Of(_Outcomes).Should().ContainSingle().Which.Tags.Should().Contain(_OutcomeTag, "succeeded");
    }

    [Fact]
    public async Task should_count_a_recovery_retry_when_a_reclaimed_run_resumes()
    {
        // given: a reclaim already charged one attempt to the row before this node picked it up
        const string function = "metrics.recovered";
        using var recorder = new MetricRecorder();
        var handler = _Handler(_HealthyManager(), _Registry(function));
        var job = _Job(function, static (_, _, _) => Task.CompletedTask, retries: 3, retryCount: 1);

        // when
        await handler.ExecuteTaskAsync(job, isDue: false, AbortToken);

        // then
        var retry = recorder.Of(_Retries).Should().ContainSingle().Subject;
        retry.Value.Should().Be(1);
        retry.Tags.Should().Contain(_RetryKindTag, "recovery");
    }

    [Fact]
    public async Task should_record_the_lateness_of_a_due_run_as_scheduled_lag()
    {
        // given: the run starts 90 seconds after its scheduled instant
        const string function = "metrics.lag";
        using var recorder = new MetricRecorder();
        var time = new FakeTimeProvider(_Now);
        var handler = _Handler(_HealthyManager(), _Registry(function), time);
        var job = _Job(function, static (_, _, _) => Task.CompletedTask);
        job.ExecutionTime = _Now.UtcDateTime.AddSeconds(-90);

        // when
        await handler.ExecuteTaskAsync(job, isDue: false, AbortToken);

        // then
        var lag = recorder.Of(_QueueLag).Should().ContainSingle().Subject;
        lag.Value.Should().Be(90);
        lag.Tags.Should().Contain(_TriggerTag, "scheduled");
    }

    [Fact]
    public async Task should_record_the_lateness_of_a_recovered_run_as_recovery_lag()
    {
        // given: a missed-run recovery that has been owed for an hour
        const string function = "metrics.recovery-lag";
        using var recorder = new MetricRecorder();
        var time = new FakeTimeProvider(_Now);
        var handler = _Handler(_HealthyManager(), _Registry(function), time);
        var job = _Job(function, static (_, _, _) => Task.CompletedTask);
        job.Type = JobType.CronJobOccurrence;
        job.ExecutionTime = _Now.UtcDateTime.AddHours(-1);
        job.RecoveredFromUtc = _Now.UtcDateTime.AddHours(-1);

        // when
        await handler.ExecuteTaskAsync(job, isDue: false, AbortToken);

        // then
        var lag = recorder.Of(_QueueLag).Should().ContainSingle().Subject;
        lag.Value.Should().Be(3600);
        lag.Tags.Should().Contain(_TriggerTag, "recovery");
    }

    [Fact]
    public async Task should_record_an_immediately_dispatched_run_as_run_now_lag()
    {
        // given
        const string function = "metrics.run-now";
        using var recorder = new MetricRecorder();
        var time = new FakeTimeProvider(_Now);
        var handler = _Handler(_HealthyManager(), _Registry(function), time);
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var dispatcher = new JobsDispatcher(taskScheduler, handler, new JobFunctionConcurrencyGate());
        var job = _Job(function, static (_, _, _) => Task.CompletedTask);
        job.ExecutionTime = _Now.UtcDateTime;

        // when
        await dispatcher.DispatchAsync([job], AbortToken);
        (await taskScheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();

        // then
        var lag = recorder.Of(_QueueLag).Should().ContainSingle().Subject;
        lag.Value.Should().Be(0);
        lag.Tags.Should().Contain(_TriggerTag, "run_now");
    }

    [Fact]
    public async Task should_tag_a_function_the_host_does_not_register_as_other()
    {
        // given: a stored row names a function this host never registered
        using var recorder = new MetricRecorder();
        var handler = _Handler(_HealthyManager(), _Registry("metrics.registered"));

        // when
        await handler.ExecuteTaskAsync(
            _Job("metrics.stray", static (_, _, _) => Task.CompletedTask),
            isDue: false,
            AbortToken
        );

        // then
        recorder.Of(_Duration).Should().ContainSingle().Which.Tags.Should().Contain(_FunctionTag, "_other");
        recorder.Of(_Outcomes).Should().ContainSingle().Which.Tags.Should().Contain(_FunctionTag, "_other");
    }

    [Fact]
    public async Task should_bound_the_function_tag_per_host_when_two_hosts_share_the_process()
    {
        // given: two hosts in one process, each registering a different function
        using var recorder = new MetricRecorder();
        await using var alphaHost = _Host<AlphaModule>();
        await using var betaHost = _Host<BetaModule>();

        // when: both run a row for the function only the first host registers
        foreach (var host in new[] { alphaHost, betaHost })
        {
            var handler = ActivatorUtilities.CreateInstance<JobsExecutionTaskHandler>(
                host,
                _HealthyManager(),
                new JobsExecutionCancellationRegistry(),
                new SchedulerOptionsBuilder()
            );
            await handler.ExecuteTaskAsync(
                _Job(AlphaModule.Function, static (_, _, _) => Task.CompletedTask),
                isDue: false,
                AbortToken
            );
        }

        // then
        recorder.Of(_Outcomes).Select(x => x.Tags[_FunctionTag]).Should().Equal(AlphaModule.Function, "_other");
    }

    [Fact]
    public async Task should_bound_the_skipped_occurrence_function_tag_with_the_same_per_host_bound()
    {
        // given: two definitions whose next occurrence overlaps one still running, one of them for a function this
        // host does not register
        using var recorder = new MetricRecorder();
        var time = new FakeTimeProvider(_Now);
        await using var host = _Host<AlphaModule>(time);
        var store = host.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        foreach (var function in new[] { AlphaModule.Function, "metrics.not-registered" })
        {
            var definition = _OverlappingDefinition(function);
            await store.InsertCronJobsAsync([definition], AbortToken);
            await store.InsertCronJobOccurrencesAsync([_RunningOccurrence(definition.Id)], AbortToken);
        }

        // when
        await host.GetRequiredService<IInternalJobManager>().GetNextJobs(AbortToken);

        // then
        recorder
            .Of(_Skipped)
            .Select(x => x.Tags[_FunctionTag])
            .Should()
            .BeEquivalentTo([AlphaModule.Function, "_other"]);
    }

    [Fact]
    public void should_subscribe_the_jobs_meter_when_the_metering_helper_is_added()
    {
        // given
        var builder = new RecordingMeterProviderBuilder();

        // when
        var returned = builder.AddJobsInstrumentation();

        // then
        returned.Should().BeSameAs(builder);
        builder.Meters.Should().Equal("Headless.Jobs");
    }

    private static readonly DateTime _Due = _Now.UtcDateTime.AddSeconds(-30);

    private static CronJobEntity _OverlappingDefinition(string function) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = "0 0 * * * *",
            ScheduleRevision = 0,
            ReconciledThroughUtc = _Due.AddHours(-1),
            NextDueUtc = _Due,
            OnMissedRun = MissedRunPolicy.Coalesce,
            MissedRunGraceSeconds = 60,
            OnOverlap = CronOverlapPolicy.Skip,
            CreatedAt = _Now.AddDays(-1),
            UpdatedAt = _Now.AddHours(-4),
        };

    private static CronJobOccurrenceEntity<CronJobEntity> _RunningOccurrence(Guid definitionId) =>
        new()
        {
            Id = Guid.NewGuid(),
            CronJobId = definitionId,
            ExecutionTime = _Due.AddHours(-1),
            Status = JobStatus.InProgress,
            OwnerId = "node-b@1",
            LockedUntil = _Now.UtcDateTime.AddMinutes(5),
            CreatedAt = _Now.AddHours(-1),
            UpdatedAt = _Now.AddHours(-1),
        };

    private static ServiceProvider _Host<TModule>(TimeProvider? time = null)
        where TModule : IJobsModule
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(time ?? TimeProvider.System);
        services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            options.AddModule<TModule>();
        });

        return services.BuildServiceProvider();
    }

    private static IInternalJobManager _HealthyManager()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        return manager;
    }

    private static JobFunctionRegistry _Registry(params string[] functions) =>
        JobFunctionRegistryBuilder.Build(
            [
                .. functions.Select(name => new KeyValuePair<string, JobFunctionRegistration>(
                    name,
                    new JobFunctionRegistration
                    {
                        CronExpression = "",
                        Priority = JobPriority.Normal,
                        Delegate = static (_, _, _) => Task.CompletedTask,
                        MaxConcurrency = 0,
                    }
                )),
            ],
            [],
            []
        );

    private static JobsExecutionTaskHandler _Handler(
        IInternalJobManager manager,
        JobFunctionRegistry registry,
        TimeProvider? time = null
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        return new JobsExecutionTaskHandler(
            services.BuildServiceProvider(),
            time ?? TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            registry,
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance,
            new JobsFunctionTagBound(registry)
        );
    }

    private static JobExecutionState _Job(
        string function,
        JobFunctionDelegate body,
        int retries = 0,
        int retryCount = 0
    ) =>
        new()
        {
            JobId = Guid.NewGuid(),
            FunctionName = function,
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            Retries = retries,
            RetryCount = retryCount,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            RunCondition = RunCondition.OnSuccess,
            CachedDelegate = body,
        };

    public sealed class AlphaModule : IJobsModule
    {
        public const string Function = "metrics.alpha";

        private AlphaModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog) => _Register(catalog, Function);
    }

    public sealed class BetaModule : IJobsModule
    {
        private BetaModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog) => _Register(catalog, "metrics.beta");
    }

    private static void _Register(JobsCatalogBuilder catalog, string function)
    {
        catalog.AddFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [function] = new()
                {
                    CronExpression = "",
                    Priority = JobPriority.Normal,
                    Delegate = static (_, _, _) => Task.CompletedTask,
                    MaxConcurrency = 0,
                },
            }
        );
    }

    [Fact]
    public async Task should_count_a_deferred_fingerprint_sweep_by_reason_without_naming_nodes()
    {
        // given
        using var recorder = new MetricRecorder();
        var unreadable = Substitute.For<INodeMembership>();
        unreadable
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns<ValueTask<IReadOnlyList<NodeLivenessSnapshot>>>(_ => throw new InvalidOperationException("down"));
        var disagreeing = Substitute.For<INodeMembership>();
        disagreeing
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(
                new ValueTask<IReadOnlyList<NodeLivenessSnapshot>>([
                    new NodeLivenessSnapshot(
                        new NodeIdentity(new NodeId("peer"), new NodeIncarnation(1)),
                        NodeLivenessState.Alive,
                        Role: null,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["headless.jobs.cron-rules-digest"] = "other-rules",
                            ["headless.jobs.fingerprint-sweep"] = "true",
                        }
                    ),
                ])
            );

        // when
        await _Agreement(unreadable).CheckAsync(AbortToken);
        await _Agreement(disagreeing).CheckAsync(AbortToken);

        // then
        recorder
            .Of(_SweepDeferred)
            .Select(x => (x.Value, Reason: x.Tags[_ReasonTag]))
            .Should()
            .Equal((1d, "membership_unavailable"), (1d, "fleet_disagreement"));
        recorder.Of(_SweepDeferred).Should().OnlyContain(x => x.Tags.Count == 1, "a node tag would be unbounded");
    }

    private const string _SweepDeferred = "headless.jobs.cron.fingerprint_sweep.deferred";
    private const string _ReasonTag = "headless.jobs.deferral_reason";

    private static CronFleetAgreement _Agreement(INodeMembership membership)
    {
        var options = new CoordinationOptions();
        options.Metadata["headless.jobs.cron-rules-digest"] = "local-rules";

        return new CronFleetAgreement(
            membership,
            Options.Create(options),
            Substitute.For<IJobsOwnerIdentity>(),
            TimeProvider.System,
            NullLogger<CronFleetAgreement>.Instance
        );
    }

    private sealed record Recorded(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);

    /// <summary>Records every measurement the Jobs meter emits while it is alive.</summary>
    private sealed class MetricRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Lock _gate = new();
        private readonly List<Recorded> _recorded = [];

        public MetricRecorder()
        {
            _listener.InstrumentPublished = static (instrument, listener) =>
            {
                if (string.Equals(instrument.Meter.Name, "Headless.Jobs", StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => _Add(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>(
                (instrument, value, tags, _) => _Add(instrument, value, tags)
            );
            _listener.Start();
        }

        public IReadOnlyList<Recorded> Of(string instrument)
        {
            lock (_gate)
            {
                return [.. _recorded.Where(x => string.Equals(x.Instrument, instrument, StringComparison.Ordinal))];
            }
        }

        public void Dispose() => _listener.Dispose();

        private void _Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            lock (_gate)
            {
                _recorded.Add(new Recorded(instrument.Name, value, copy));
            }
        }
    }

    private sealed class RecordingMeterProviderBuilder : MeterProviderBuilder
    {
        public List<string> Meters { get; } = [];

        public override MeterProviderBuilder AddInstrumentation<TInstrumentation>(
            Func<TInstrumentation> instrumentationFactory
        ) => this;

        public override MeterProviderBuilder AddMeter(params string[] names)
        {
            Meters.AddRange(names);
            return this;
        }
    }
}
