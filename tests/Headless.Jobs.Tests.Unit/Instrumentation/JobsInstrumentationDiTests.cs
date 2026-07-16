// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Instrumentation;

/// <summary>
/// Pins the DI wiring behind #695's single opt-in: <c>AddHeadlessJobs</c> registers
/// <c>LoggerInstrumentation</c> as the default <c>IJobsInstrumentation</c>, and that default emits
/// <see cref="Activity"/> spans as soon as a builder-side listener subscribes to
/// <see cref="JobsDiagnostics.SourceName"/> — no separate swap-implementation registration exists anymore.
/// A consumer-registered <c>IJobsInstrumentation</c> must still take priority over the default.
/// </summary>
public sealed class JobsInstrumentationDiTests
{
    [Fact]
    public void default_registration_is_logger_instrumentation()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());

        // when
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IJobsInstrumentation>().Should().BeOfType<LoggerInstrumentation>();
    }

    [Fact]
    public void default_registration_emits_a_job_activity_once_a_builder_side_listener_is_attached()
    {
        // given — the only opt-in is the ActivityListener/TracerProviderBuilder subscribing to
        // JobsDiagnostics.SourceName; AddHeadlessJobs is otherwise untouched (no swap call anywhere).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());

        using var provider = services.BuildServiceProvider();
        var instrumentation = provider.GetRequiredService<IJobsInstrumentation>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, JobsDiagnostics.SourceName, StringComparison.Ordinal),
            Sample = static (ref _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            Type = JobType.TimeJob,
            FunctionName = "SendEmail",
            CachedPriority = JobPriority.Normal,
        };

        // when
        using var activity = instrumentation.StartJobActivity("job.execute", context);

        // then
        activity.Should().NotBeNull();
        activity!.GetTagItem("headless.job.id").Should().Be(context.JobId.ToString());
        activity.GetTagItem("headless.job.function").Should().Be("SendEmail");
    }

    [Fact]
    public void a_custom_instrumentation_registered_after_add_headless_jobs_wins_over_the_default()
    {
        // given — the framework default only wins when nothing else was registered; a consumer swapping
        // IJobsInstrumentation for their own implementation must still be able to.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        var custom = Substitute.For<IJobsInstrumentation>();
        services.AddSingleton(custom);

        // when
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IJobsInstrumentation>().Should().BeSameAs(custom);
    }
}
