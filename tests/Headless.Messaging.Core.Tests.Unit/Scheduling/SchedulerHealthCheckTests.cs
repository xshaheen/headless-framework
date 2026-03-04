// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Scheduling;
using Headless.Testing.Tests;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Scheduling;

public sealed class SchedulerHealthCheckTests : TestBase
{
    private readonly IScheduledJobStorage _storage = Substitute.For<IScheduledJobStorage>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly IOptions<SchedulerOptions> _options = Options.Create(
        new SchedulerOptions { StaleJobThreshold = TimeSpan.FromMinutes(5) }
    );

    [Fact]
    public async Task should_return_healthy_when_storage_reachable_and_no_stale_jobs()
    {
        // given
        var job = _CreateJob("healthy-job");
        job.Status = ScheduledJobStatus.Pending;
        job.DateLocked = null;

        _storage.GetAllJobsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScheduledJob>)[job]);

        var sut = new SchedulerHealthCheck(_storage, _options, _timeProvider);

        // when
        var result = await sut.CheckHealthAsync(new HealthCheckContext(), AbortToken);

        // then
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("reachable");
        result.Data["stale_jobs"].Should().Be(0);
        result.Data["total_jobs"].Should().Be(1);
    }

    [Fact]
    public async Task should_return_degraded_when_stale_jobs_exist()
    {
        // given
        var staleJob = _CreateJob("stale-job");
        staleJob.Status = ScheduledJobStatus.Running;
        staleJob.DateLocked = _timeProvider.GetUtcNow().AddMinutes(-10);

        var healthyJob = _CreateJob("healthy-job");
        healthyJob.Status = ScheduledJobStatus.Pending;
        healthyJob.DateLocked = null;

        _storage
            .GetAllJobsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ScheduledJob>)[staleJob, healthyJob]);

        var sut = new SchedulerHealthCheck(_storage, _options, _timeProvider);

        // when
        var result = await sut.CheckHealthAsync(new HealthCheckContext(), AbortToken);

        // then
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("1 stale");
        result.Data["stale_jobs"].Should().Be(1);
        result.Data["total_jobs"].Should().Be(2);
    }

    [Fact]
    public async Task should_return_unhealthy_when_storage_throws()
    {
        // given
        _storage
            .GetAllJobsAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ScheduledJob>>(_ => throw new InvalidOperationException("storage error"));

        var sut = new SchedulerHealthCheck(_storage, _options, _timeProvider);

        // when
        var result = await sut.CheckHealthAsync(new HealthCheckContext(), AbortToken);

        // then
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("failed");
        result.Exception.Should().BeOfType<InvalidOperationException>();
    }

    // -- helpers --

    private ScheduledJob _CreateJob(string name)
    {
        var now = _timeProvider.GetUtcNow();
        return new ScheduledJob
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = ScheduledJobType.Recurring,
            CronExpression = "0 0 0 * * *",
            TimeZone = "UTC",
            Status = ScheduledJobStatus.Pending,
            NextRunTime = now.AddDays(1),
            MaxRetries = 0,
            SkipIfRunning = false,
            IsEnabled = true,
            DateCreated = now,
            DateUpdated = now,
            MisfireStrategy = MisfireStrategy.FireImmediately,
        };
    }
}
