// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Scheduling;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Scheduling;

public sealed class StaleJobRecoveryServiceTests : TestBase
{
    private readonly IScheduledJobStorage _storage = Substitute.For<IScheduledJobStorage>();
    private readonly ILogger<StaleJobRecoveryService> _logger;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly IOptions<SchedulerOptions> _options = Options.Create(
        new SchedulerOptions
        {
            StaleJobThreshold = TimeSpan.FromMinutes(5),
            StaleJobCheckInterval = TimeSpan.FromMilliseconds(10),
            ExecutionRetention = TimeSpan.FromDays(7),
        }
    );

    public StaleJobRecoveryServiceTests()
    {
        _logger = LoggerFactory.CreateLogger<StaleJobRecoveryService>();
    }

    [Fact]
    public async Task should_release_stale_jobs_at_configured_interval()
    {
        // given
        _storage.ReleaseStaleJobsAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(3);
        _storage.TimeoutStaleExecutionsAsync(Arg.Any<CancellationToken>()).Returns(0);
        _storage.PurgeExecutionsAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(0);

        var sut = new StaleJobRecoveryService(_storage, _logger, _options);

        // when
        using var cts = new CancellationTokenSource();
        var startTask = sut.StartAsync(cts.Token);
        await Task.Delay(100, CancellationToken.None);
        await cts.CancelAsync();
        await startTask.ConfigureAwait(false);

        // then
        await _storage
            .Received()
            .ReleaseStaleJobsAsync(
                Arg.Is<TimeSpan>(t => t == _options.Value.StaleJobThreshold),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_timeout_stale_executions_after_releasing_stale_jobs()
    {
        // given
        _storage.ReleaseStaleJobsAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(2);
        _storage.TimeoutStaleExecutionsAsync(Arg.Any<CancellationToken>()).Returns(2);
        _storage.PurgeExecutionsAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(0);

        var sut = new StaleJobRecoveryService(_storage, _logger, _options);

        // when
        using var cts = new CancellationTokenSource();
        var startTask = sut.StartAsync(cts.Token);
        await Task.Delay(100, CancellationToken.None);
        await cts.CancelAsync();
        await startTask.ConfigureAwait(false);

        // then
        await _storage.Received().TimeoutStaleExecutionsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_purge_old_executions_at_configured_retention()
    {
        // given
        _storage.ReleaseStaleJobsAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(0);
        _storage.TimeoutStaleExecutionsAsync(Arg.Any<CancellationToken>()).Returns(0);
        _storage.PurgeExecutionsAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(10);

        var sut = new StaleJobRecoveryService(_storage, _logger, _options);

        // when
        using var cts = new CancellationTokenSource();
        var startTask = sut.StartAsync(cts.Token);
        await Task.Delay(100, CancellationToken.None);
        await cts.CancelAsync();
        await startTask.ConfigureAwait(false);

        // then
        await _storage
            .Received()
            .PurgeExecutionsAsync(
                Arg.Is<TimeSpan>(t => t == _options.Value.ExecutionRetention),
                Arg.Any<CancellationToken>()
            );
    }
}
