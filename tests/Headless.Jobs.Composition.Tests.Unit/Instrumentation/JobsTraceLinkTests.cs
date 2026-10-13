// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless;
using Headless.Jobs;
using Headless.Jobs.Infrastructure.Dashboard;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Instrumentation;

/// <summary>
/// A job's execution is its own trace, linked back to the span that scheduled it, through the real scheduler, managers,
/// in-memory store, dashboard repository, and execution handler.
/// </summary>
[Collection<JobsInstrumentationCollection>]
public sealed class JobsTraceLinkTests : TestBase
{
    private const string _Function = "trace.link";
    private const string _ScheduleSpan = "job.schedule";
    private const string _TimeJobSpan = "job.execute.timejob";
    private const string _OccurrenceSpan = "job.execute.cronjoboccurrence";
    private const string _RequestSourceName = "Tests.Jobs.TraceLink";

    private static readonly ActivitySource _Requests = new(_RequestSourceName);

    [Fact]
    public async Task should_link_the_execution_span_to_the_schedule_span_of_the_request_that_scheduled_it()
    {
        // given: a request span schedules a time job that later runs on its own, so the execution must link back to it.
        using var recorder = SpanRecorder.All();
        await using var harness = Harness.Create();
        Guid jobId;
        ActivityContext request;

        using (var requestSpan = _Requests.StartActivity("request"))
        {
            request = requestSpan!.Context;

            // when
            jobId = await harness.Scheduler.ScheduleAsync<TraceJob>(harness.Now.AddMinutes(1), AbortToken);
        }

        await harness.RunDueAsync(TimeSpan.FromMinutes(1));

        // then
        var schedule = recorder.Single(_ScheduleSpan);
        schedule.Kind.Should().Be(ActivityKind.Producer);
        schedule.TraceId.Should().Be(request.TraceId, "the schedule span is part of the request's trace");
        schedule.ParentSpanId.Should().Be(request.SpanId);
        (await harness.TimeJobAsync(jobId)).TraceParent.Should().Be(_TraceParent(schedule));

        var execution = recorder.Single(_TimeJobSpan);
        execution.TraceId.Should().NotBe(request.TraceId, "the execution starts its own trace");
        execution.ParentSpanId.Should().Be(default(ActivitySpanId), "the execution span is a root");
        _ShouldLinkOnlyTo(execution, schedule.Context);
        recorder.Names.Should().NotContain("job.enqueue", "scheduling, not execution, emits the scheduling span");
        (await harness.TimeJobAsync(jobId)).Status.Should().Be(JobStatus.Succeeded);
    }

    [Fact]
    public async Task should_store_no_trace_context_and_start_an_unlinked_trace_when_nothing_was_traced_at_scheduling()
    {
        // given: no listener and no ambient span while scheduling
        await using var harness = Harness.Create();
        var jobId = await harness.Scheduler.ScheduleAsync<TraceJob>(harness.Now.AddMinutes(1), AbortToken);

        // then
        (await harness.TimeJobAsync(jobId))
            .TraceParent.Should()
            .BeNull("a context is never invented");

        // when the run is traced
        using var recorder = SpanRecorder.All();
        await harness.RunDueAsync(TimeSpan.FromMinutes(1));

        var execution = recorder.Single(_TimeJobSpan);
        execution.Links.Should().BeEmpty();
        execution.ParentSpanId.Should().Be(default(ActivitySpanId));
    }

    [Fact]
    public async Task should_store_the_ambient_context_when_no_listener_creates_the_schedule_span()
    {
        // given: only the request source is traced, so the Jobs source creates no schedule span
        using var recorder = SpanRecorder.RequestsOnly();
        await using var harness = Harness.Create();
        Guid jobId;
        string? requestTraceParent;

        using (var requestSpan = _Requests.StartActivity("request"))
        {
            requestTraceParent = _TraceParent(requestSpan!);

            // when
            jobId = await harness.Scheduler.EnqueueAsync<TraceJob>(AbortToken);
        }

        // then
        recorder.Names.Should().NotContain(_ScheduleSpan);
        (await harness.TimeJobAsync(jobId)).TraceParent.Should().Be(requestTraceParent);
    }

    [Fact]
    public async Task should_keep_the_original_context_on_an_ordinary_update()
    {
        // given
        using var recorder = SpanRecorder.All();
        await using var harness = Harness.Create();
        Guid jobId;

        using (_Requests.StartActivity("original"))
        {
            jobId = await harness.Scheduler.ScheduleAsync<TraceJob>(harness.Now.AddMinutes(5), AbortToken);
        }

        var original = (await harness.TimeJobAsync(jobId)).TraceParent;
        original.Should().NotBeNull();

        // when: another request edits the row
        using (_Requests.StartActivity("edit"))
        {
            var edited = await harness.TimeJobAsync(jobId);
            edited.Description = "edited";
            (await harness.TimeJobs.UpdateAsync(edited, AbortToken)).IsSucceeded.Should().BeTrue();
        }

        // then
        var stored = await harness.TimeJobAsync(jobId);
        stored.Description.Should().Be("edited");
        stored.TraceParent.Should().Be(original, "an edit is not a new cause");
    }

    [Fact]
    public async Task should_replace_the_context_with_the_superseding_call_on_a_keyed_replace()
    {
        // given
        using var recorder = SpanRecorder.All();
        await using var harness = Harness.Create();
        var key = new JobKey("order-42");
        JobScheduleResult first;

        using (_Requests.StartActivity("first"))
        {
            first = await harness.Scheduler.ScheduleKeyedAsync<TraceJob>(key, harness.Now.AddMinutes(5), AbortToken);
        }

        var firstSchedule = recorder.Single(_ScheduleSpan);

        // when
        JobScheduleResult second;
        using (_Requests.StartActivity("supersede"))
        {
            second = await harness.Scheduler.ReplaceKeyedAsync<TraceJob>(
                key,
                first.Generation!.Value,
                harness.Now.AddMinutes(10),
                AbortToken
            );
        }

        // then
        second.Disposition.Should().Be(JobScheduleDisposition.Replaced);
        var superseding = recorder.All(_ScheduleSpan).Single(x => x.SpanId != firstSchedule.SpanId);
        (await harness.TimeJobAsync(first.RunId!.Value)).TraceParent.Should().Be(_TraceParent(firstSchedule));
        (await harness.TimeJobAsync(second.RunId!.Value)).TraceParent.Should().Be(_TraceParent(superseding));
    }

    [Fact]
    public async Task should_replace_the_context_with_the_requeue_request()
    {
        // given: a time job that ran and failed
        using var recorder = SpanRecorder.All();
        await using var harness = Harness.Create(failing: true);
        Guid jobId;

        using (_Requests.StartActivity("original"))
        {
            jobId = await harness.Scheduler.ScheduleAsync<TraceJob>(harness.Now.AddMinutes(1), AbortToken);
        }

        await harness.RunDueAsync(TimeSpan.FromMinutes(1));
        (await harness.TimeJobAsync(jobId)).Status.Should().Be(JobStatus.Failed);
        var original = (await harness.TimeJobAsync(jobId)).TraceParent;

        // when
        using (_Requests.StartActivity("requeue"))
        {
            (await harness.Scheduler.RequeueAsync(jobId, AbortToken)).Should().Be(JobRequeueOutcome.Requeued);
        }

        // then
        var requeue = recorder.All(_ScheduleSpan).Last();
        var stored = await harness.TimeJobAsync(jobId);
        stored.TraceParent.Should().Be(_TraceParent(requeue)).And.NotBe(original);
    }

    [Fact]
    public async Task should_store_no_context_on_a_recurring_definition_or_the_occurrences_the_scheduler_generates()
    {
        // given: a recurring schedule made under a request
        using var recorder = SpanRecorder.All();
        await using var harness = Harness.Create();

        using (_Requests.StartActivity("request"))
        {
            await harness.Scheduler.ScheduleRecurringAsync<TraceJob>("0 * * * * *", AbortToken);
        }

        // when the scheduler materializes and runs the next occurrence
        await harness.RunDueAsync(TimeSpan.FromMinutes(1));

        // then
        var occurrence = (await harness.OccurrencesAsync()).Should().ContainSingle().Subject;
        occurrence.TraceParent.Should().BeNull("no request caused a generated occurrence");
        recorder.Single(_OccurrenceSpan).Links.Should().BeEmpty();
    }

    [Fact]
    public async Task should_link_a_dashboard_run_now_occurrence_to_the_dashboard_request()
    {
        // given
        using var recorder = SpanRecorder.All();
        await using var harness = Harness.Create();
        var definitionId = await harness.Scheduler.ScheduleRecurringAsync<TraceJob>("0 0 0 1 1 *", AbortToken);
        var dispatched = new List<JobExecutionState>();
        var repository = harness.DashboardRepository(dispatched);

        // when
        using (_Requests.StartActivity("dashboard"))
        {
            await repository.AddOnDemandCronJobOccurrenceAsync(definitionId, AbortToken);
        }

        await harness.ExecuteAsync(dispatched);

        // then
        var schedule = recorder.Single(_ScheduleSpan);
        var occurrence = (await harness.OccurrencesAsync()).Should().ContainSingle().Subject;
        occurrence.TraceParent.Should().Be(_TraceParent(schedule));
        _ShouldLinkOnlyTo(recorder.Single(_OccurrenceSpan), schedule.Context);
    }

    [Fact]
    public async Task should_start_an_inline_chain_child_as_its_own_trace_linked_to_the_chain_schedule_span()
    {
        // given
        using var recorder = SpanRecorder.All();
        await using var harness = Harness.Create();
        var chain = JobChain.Start<TraceJob>();
        chain.Root.Then<TraceJob>();

        using (_Requests.StartActivity("request"))
        {
            await harness.Scheduler.EnqueueAsync(chain.Build(), AbortToken);
        }

        // when
        await harness.RunDueAsync(TimeSpan.Zero);

        // then
        var schedule = recorder.Single(_ScheduleSpan);
        var executions = recorder.All(_TimeJobSpan).ToList();
        executions.Should().HaveCount(2, "the parent and its inline child both ran");
        var parent = executions.Single(x => Equals(x.GetTagItem("headless.job.is_child"), false));
        var child = executions.Single(x => Equals(x.GetTagItem("headless.job.is_child"), true));
        child.ParentSpanId.Should().Be(default(ActivitySpanId), "the child is not a child of its parent's execution");
        child.TraceId.Should().NotBe(parent.TraceId);
        _ShouldLinkOnlyTo(child, schedule.Context);
        _ShouldLinkOnlyTo(parent, schedule.Context);
    }

    private static string _TraceParent(Activity activity)
    {
        // The W3C header form, written out here rather than read from the code under test.
        var flags = activity.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ? "01" : "00";
        return $"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-{flags}";
    }

    private static void _ShouldLinkOnlyTo(Activity execution, ActivityContext scheduledBy)
    {
        var link = execution.Links.Should().ContainSingle().Subject;
        link.Context.TraceId.Should().Be(scheduledBy.TraceId);
        link.Context.SpanId.Should().Be(scheduledBy.SpanId);
    }

    public sealed class TraceJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class TraceJobsModule : IJobsModule
    {
        private TraceJobsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog) =>
            _Register(catalog, static (_, _, _) => Task.CompletedTask);
    }

    public sealed class FailingTraceJobsModule : IJobsModule
    {
        private FailingTraceJobsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog) =>
            _Register(catalog, static (_, _, _) => throw new InvalidOperationException("the job fails"));
    }

    private static void _Register(JobsCatalogBuilder catalog, JobFunctionDelegate body)
    {
        catalog.AddFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [_Function] = new()
                {
                    CronExpression = "",
                    Priority = JobPriority.Normal,
                    Delegate = body,
                    MaxConcurrency = 0,
                    JobType = typeof(TraceJob),
                },
            }
        );
        catalog.AddDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                [_Function] = new(_Function, null, "", JobPriority.Normal, 0),
            }
        );
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly FakeTimeProvider _time;

        private Harness(ServiceProvider provider, FakeTimeProvider time)
        {
            _provider = provider;
            _time = time;
        }

        public IJobScheduler Scheduler => _provider.GetRequiredService<IJobScheduler>();

        public ITimeJobManager<TimeJobEntity> TimeJobs =>
            _provider.GetRequiredService<ITimeJobManager<TimeJobEntity>>();

        public DateTimeOffset Now => _time.GetUtcNow();

        private IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store =>
            _provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        public static Harness Create(bool failing = false)
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(time);
            services.AddHeadlessJobs(options =>
            {
                options.DisableBackgroundServices();
                _ = failing ? options.AddModule<FailingTraceJobsModule>() : options.AddModule<TraceJobsModule>();
            });

            return new Harness(services.BuildServiceProvider(), time);
        }

        /// <summary>
        /// Advances the clock, claims every due run the way the scheduler loop does, and runs each claim. An unrelated
        /// span is ambient while it runs, standing in for the poll loop's own context, which must not become the
        /// execution's parent.
        /// </summary>
        public async Task RunDueAsync(TimeSpan advance)
        {
            _time.Advance(advance);
            var (_, claimed) = await _provider.GetRequiredService<IInternalJobManager>().GetNextJobs(AbortToken);
            claimed.Should().NotBeEmpty("a run is due");
            var registry = _provider.GetRequiredService<JobFunctionRegistry>();
            var manager = _provider.GetRequiredService<IInternalJobManager>();
            var admitted = new List<JobExecutionState>();
            foreach (var state in claimed)
            {
                // The hydration and admission steps the dispatcher performs before a run starts.
                JobsExecutionContext.CacheFunctionReferences(state, registry);
                admitted.AddRange(await manager.SetTickersInProgress([state], AbortToken));
            }

            admitted.Should().HaveSameCount(claimed);
            await ExecuteAsync(admitted);
        }

        /// <summary>Runs already admitted states, as the dispatcher's worker does.</summary>
        public async Task ExecuteAsync(IReadOnlyCollection<JobExecutionState> admitted)
        {
            var handler = ActivatorUtilities.CreateInstance<JobsExecutionTaskHandler>(
                _provider,
                new JobsExecutionCancellationRegistry(),
                new SchedulerOptionsBuilder()
            );

            using var ambient = _Requests.StartActivity("poll-loop");
            foreach (var state in admitted)
            {
                await handler.ExecuteTaskAsync(state, isDue: false, AbortToken);
            }
        }

        public async Task<TimeJobEntity> TimeJobAsync(Guid id)
        {
            var stored = await _Store.GetTimeJobByIdAsync(id, AbortToken);
            stored.Should().NotBeNull();
            return stored!;
        }

        public async Task<CronJobOccurrenceEntity<CronJobEntity>[]> OccurrencesAsync() =>
            await _Store.GetAllCronJobOccurrencesAsync(predicate: null, AbortToken);

        public JobsDashboardRepository<TimeJobEntity, CronJobEntity> DashboardRepository(
            List<JobExecutionState> dispatched
        )
        {
            var dispatcher = Substitute.For<IJobsDispatcher>();
            dispatcher
                .DispatchAsync(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask)
                .AndDoes(call => dispatched.AddRange(call.Arg<JobExecutionState[]>()));

            return new JobsDashboardRepository<TimeJobEntity, CronJobEntity>(
                new JobsExecutionContext(),
                _Store,
                Substitute.For<IJobsHostScheduler>(),
                Substitute.For<IJobsNotificationHubSender>(),
                new DashboardOptionsBuilder(),
                dispatcher,
                _provider.GetRequiredService<JobFunctionRegistry>(),
                _time,
                _provider.GetRequiredService<IGuidGenerator>(),
                _provider,
                JobsRequestSerializationOptions.Default
            );
        }

        public ValueTask DisposeAsync() => _provider.DisposeAsync();
    }

    private sealed class SpanRecorder : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly ConcurrentQueue<Activity> _stopped = new();

        private SpanRecorder(Func<ActivitySource, bool> listenTo)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = listenTo,
                Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public static SpanRecorder All() =>
            new(static source => source.Name is JobsDiagnostics.SourceName or _RequestSourceName);

        public static SpanRecorder RequestsOnly() => new(static source => source.Name is _RequestSourceName);

        public IEnumerable<string> Names => _stopped.Select(x => x.OperationName);

        public IEnumerable<Activity> All(string name) =>
            _stopped.Where(x => string.Equals(x.OperationName, name, StringComparison.Ordinal));

        public Activity Single(string name) =>
            _stopped
                .Should()
                .ContainSingle(x => string.Equals(x.OperationName, name, StringComparison.Ordinal))
                .Subject;

        public void Dispose() => _listener.Dispose();
    }
}
