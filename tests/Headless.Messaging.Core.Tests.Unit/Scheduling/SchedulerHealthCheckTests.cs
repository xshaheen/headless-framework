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
        _storage.GetStaleJobCountAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(0);

        var sut = new SchedulerHealthCheck(_storage, _options, _timeProvider);

        // when
        var result = await sut.CheckHealthAsync(new HealthCheckContext(), AbortToken);

        // then
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("reachable");
        result.Data["stale_jobs"].Should().Be(0);
    }

    [Fact]
    public async Task should_return_degraded_when_stale_jobs_exist()
    {
        // given
        _storage.GetStaleJobCountAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(1);

        var sut = new SchedulerHealthCheck(_storage, _options, _timeProvider);

        // when
        var result = await sut.CheckHealthAsync(new HealthCheckContext(), AbortToken);

        // then
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("1 stale");
        result.Data["stale_jobs"].Should().Be(1);
    }

    [Fact]
    public async Task should_return_unhealthy_when_storage_throws()
    {
        // given
        _storage
            .GetStaleJobCountAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("storage error"));

        var sut = new SchedulerHealthCheck(_storage, _options, _timeProvider);

        // when
        var result = await sut.CheckHealthAsync(new HealthCheckContext(), AbortToken);

        // then
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("failed");
        result.Exception.Should().BeOfType<InvalidOperationException>();
    }
}
