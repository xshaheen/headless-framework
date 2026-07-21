// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests.Instrumentation;

/// <summary>
/// Pins <c>LoggerInstrumentation</c> as the single default <c>IJobsInstrumentation</c>: it must emit
/// <see cref="Activity"/> spans only when a listener is attached to <see cref="JobsDiagnostics.SourceName"/>
/// (no separate swap-implementation step exists anymore, per #695), and it must keep logging unconditionally
/// regardless of listener presence.
/// </summary>
public sealed class LoggerInstrumentationTests : TestBase
{
    [Fact]
    public void start_job_activity_returns_null_when_no_listener_is_attached()
    {
        // given
        var instrumentation = new LoggerInstrumentation(
            Substitute.For<ILogger<LoggerInstrumentation>>(),
            new StubOwnerIdentity()
        );
        var context = new JobExecutionState { JobId = Guid.NewGuid(), FunctionName = "SendEmail" };

        // when
        var activity = instrumentation.StartJobActivity("job.execute", context);

        // then
        activity.Should().BeNull();
    }

    [Fact]
    public void start_job_activity_produces_activity_with_expected_tags_when_listener_is_attached()
    {
        // given — builder-side subscription alone (an ActivityListener on JobsDiagnostics.SourceName) must
        // be enough; there is no second opt-in step to invoke.
        var instrumentation = new LoggerInstrumentation(
            Substitute.For<ILogger<LoggerInstrumentation>>(),
            new StubOwnerIdentity()
        );
        using var listener = _StartListener();

        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            ParentId = Guid.NewGuid(),
            Type = JobType.TimeJob,
            FunctionName = "SendEmail",
            CachedPriority = JobPriority.High,
            Retries = 3,
            RunCondition = RunCondition.OnFailure,
        };

        // when
        using var activity = instrumentation.StartJobActivity("job.execute", context);

        // then
        activity.Should().NotBeNull();
        activity!.GetTagItem("headless.job.id").Should().Be(context.JobId.ToString());
        activity.GetTagItem("headless.job.type").Should().Be(context.Type.ToString());
        activity.GetTagItem("headless.job.function").Should().Be(context.FunctionName);
        activity.GetTagItem("headless.job.priority").Should().Be(context.CachedPriority.ToString());
        activity.GetTagItem("headless.job.machine").Should().Be(StubOwnerIdentity.Owner);
        activity.GetTagItem("headless.job.retry_count").Should().Be(context.Retries);
        activity.GetTagItem("headless.job.parent_id").Should().Be(context.ParentId!.Value.ToString());
        activity.GetTagItem("headless.job.run_condition").Should().Be(context.RunCondition.ToString());
    }

    [Fact]
    public void start_job_activity_omits_run_condition_tag_for_cron_occurrences()
    {
        // given — the run_condition tag only applies to time jobs (a cron occurrence has no parent-relative
        // run condition), mirroring the pre-unification OpenTelemetryInstrumentation carve-out.
        var instrumentation = new LoggerInstrumentation(
            Substitute.For<ILogger<LoggerInstrumentation>>(),
            new StubOwnerIdentity()
        );
        using var listener = _StartListener();

        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            Type = JobType.CronJobOccurrence,
            FunctionName = "SendDigest",
        };

        // when
        using var activity = instrumentation.StartJobActivity("job.execute", context);

        // then
        activity.Should().NotBeNull();
        activity!.GetTagItem("headless.job.run_condition").Should().BeNull();
        activity.GetTagItem("headless.job.parent_id").Should().BeNull();
    }

    [Fact]
    public void log_job_enqueued_logs_even_without_a_listener()
    {
        // given
        var logger = Substitute.For<ILogger<LoggerInstrumentation>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var instrumentation = new LoggerInstrumentation(logger, new StubOwnerIdentity());
        var jobId = Guid.NewGuid();

        // when
        instrumentation.LogJobEnqueued("Time", "SendEmail", jobId, "caller-info");

        // then — the log call fires regardless of whether anything is listening to the activity source.
        logger
            .ReceivedCalls()
            .Should()
            .Contain(call =>
                call.GetMethodInfo().Name == "Log" && call.GetArguments().OfType<EventId>().Any(e => e.Id == 1000)
            );
    }

    [Fact]
    public void log_job_enqueued_wraps_the_log_in_an_activity_span_when_listener_is_attached()
    {
        // given
        var logger = Substitute.For<ILogger<LoggerInstrumentation>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var instrumentation = new LoggerInstrumentation(logger, new StubOwnerIdentity());
        var activities = new ConcurrentBag<Activity>();
        using var listener = _StartListener(activities.Add);
        var jobId = Guid.NewGuid();

        // when
        instrumentation.LogJobEnqueued("Time", "SendEmail", jobId, "caller-info");

        // then — both the span and the underlying log fire.
        var activity = activities.Should().ContainSingle(a => a.OperationName == "job.enqueue").Subject;
        activity.GetTagItem("headless.job.id").Should().Be(jobId.ToString());
        activity.GetTagItem("headless.job.type").Should().Be("Time");
        activity.GetTagItem("headless.job.function").Should().Be("SendEmail");
        activity.GetTagItem("headless.job.enqueued_from").Should().Be("caller-info");

        logger
            .ReceivedCalls()
            .Should()
            .Contain(call =>
                call.GetMethodInfo().Name == "Log" && call.GetArguments().OfType<EventId>().Any(e => e.Id == 1000)
            );
    }

    [Fact]
    public void log_job_failed_sets_error_status_and_exception_tags_when_listener_is_attached()
    {
        // given
        var instrumentation = new LoggerInstrumentation(
            Substitute.For<ILogger<LoggerInstrumentation>>(),
            new StubOwnerIdentity()
        );
        var activities = new ConcurrentBag<Activity>();
        using var listener = _StartListener(activities.Add);
        var jobId = Guid.NewGuid();
        var exception = new InvalidOperationException("boom");

        // when
        instrumentation.LogJobFailed(jobId, "SendEmail", exception, retryCount: 2);

        // then
        var activity = activities.Should().ContainSingle(a => a.OperationName == "job.fail").Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem("headless.job.retry_count").Should().Be(2);
        activity.GetTagItem("exception.type").Should().Be(nameof(InvalidOperationException));
        activity.GetTagItem("exception.message").Should().Be("boom");
    }

    private static ActivityListener _StartListener(Action<Activity>? onStopped = null)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, JobsDiagnostics.SourceName, StringComparison.Ordinal),
            Sample = static (ref _) => ActivitySamplingResult.AllData,
            ActivityStopped = onStopped,
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    private sealed class StubOwnerIdentity : IJobsOwnerIdentity
    {
        public const string Owner = "test-node@1";

        public string DisplayOwner => Owner;

        public CancellationToken MembershipLostToken => CancellationToken.None;

        public bool TryGetStampOwner([NotNullWhen(true)] out string? owner)
        {
            owner = Owner;

            return true;
        }
    }
}
