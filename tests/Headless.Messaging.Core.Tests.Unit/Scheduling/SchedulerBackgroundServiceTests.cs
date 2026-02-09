// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.Scheduling;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Scheduling;

public sealed class SchedulerBackgroundServiceTests : TestBase, IDisposable
{
    private static readonly IReadOnlyList<ScheduledJob> _EmptyJobs = [];

    private readonly IScheduledJobStorage _storage = Substitute.For<IScheduledJobStorage>();
    private readonly StubDispatcher _dispatcher = new();
    private readonly CronScheduleCache _cronCache = new();

    public void Dispose() => _cronCache.Dispose();

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly ILogger<SchedulerBackgroundService> _logger;
    private readonly IOptions<SchedulerOptions> _options = Options.Create(
        new SchedulerOptions { PollingInterval = TimeSpan.FromMilliseconds(10) }
    );

    public SchedulerBackgroundServiceTests()
    {
        _logger = LoggerFactory.CreateLogger<SchedulerBackgroundService>();
    }

    [Fact]
    public async Task should_acquire_and_dispatch_due_jobs_when_polling()
    {
        // given
        var job = _CreateRecurringJob();
        _SetupAcquireOnce(job);
        var sut = _CreateService();

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then
        _dispatcher.DispatchedJobs.Should().ContainSingle(j => j.Name == job.Name);
    }

    [Fact]
    public async Task should_handle_dispatch_failure_and_record_failed_execution()
    {
        // given
        var job = _CreateRecurringJob();
        job.RetryIntervals = null;
        _SetupAcquireOnce(job);
        _dispatcher.ExceptionToThrow = new InvalidOperationException("handler error");
        var sut = _CreateService();

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then
        await _storage
            .Received()
            .UpdateExecutionAsync(
                Arg.Is<JobExecution>(e => e.Status == JobExecutionStatus.Failed && e.Error!.Contains("handler error")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_compute_next_run_time_for_recurring_jobs_after_success()
    {
        // given
        var job = _CreateRecurringJob();
        _SetupAcquireOnce(job);
        var sut = _CreateService();

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then
        await _storage
            .Received()
            .UpdateJobAsync(
                Arg.Is<ScheduledJob>(j =>
                    j.NextRunTime != null && j.Status == ScheduledJobStatus.Pending && j.RetryCount == 0
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_mark_one_time_jobs_completed_after_success()
    {
        // given
        var job = _CreateOneTimeJob();
        _SetupAcquireOnce(job);
        var sut = _CreateService();

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then
        await _storage
            .Received()
            .UpdateJobAsync(
                Arg.Is<ScheduledJob>(j => j.Status == ScheduledJobStatus.Completed && j.NextRunTime == null),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_apply_retry_intervals_when_job_fails()
    {
        // given
        var job = _CreateRecurringJob();
        job.RetryIntervals = [1000, 5000, 30000];
        job.RetryCount = 0;
        _SetupAcquireOnce(job);
        _dispatcher.ExceptionToThrow = new InvalidOperationException("fail");
        var sut = _CreateService();

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then — retry count incremented, next run set to now + first retry interval (1000ms)
        await _storage
            .Received()
            .UpdateJobAsync(
                Arg.Is<ScheduledJob>(j =>
                    j.RetryCount == 1 && j.Status == ScheduledJobStatus.Pending && j.NextRunTime != null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_reset_retry_count_for_recurring_jobs_without_retry_intervals()
    {
        // given
        var job = _CreateRecurringJob();
        job.RetryIntervals = null;
        job.RetryCount = 2;
        _SetupAcquireOnce(job);
        _dispatcher.ExceptionToThrow = new InvalidOperationException("fail");
        var sut = _CreateService();

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then — for recurring without retry intervals: reset retry count, compute next cron occurrence
        await _storage
            .Received()
            .UpdateJobAsync(
                Arg.Is<ScheduledJob>(j =>
                    j.RetryCount == 0 && j.Status == ScheduledJobStatus.Pending && j.NextRunTime != null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_skip_job_when_distributed_lock_unavailable_and_skip_if_running()
    {
        // given
        var job = _CreateRecurringJob();
        job.SkipIfRunning = true;
        _SetupAcquireOnce(job);

        var lockProvider = Substitute.For<IDistributedLockProvider>();
        lockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns((IDistributedLock?)null);

        var sut = _CreateService(lockProvider);

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then — job released back to pending, dispatch never called
        _dispatcher.DispatchedJobs.Should().BeEmpty();
        await _storage
            .Received()
            .UpdateJobAsync(
                Arg.Is<ScheduledJob>(j => j.Status == ScheduledJobStatus.Pending && j.LockHolder == null),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_proceed_when_no_distributed_lock_provider_registered()
    {
        // given — no lock provider (default behavior)
        var job = _CreateRecurringJob();
        job.SkipIfRunning = true;
        _SetupAcquireOnce(job);
        var sut = _CreateService(lockProvider: null);

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then — dispatch should proceed even with SkipIfRunning=true
        _dispatcher.DispatchedJobs.Should().ContainSingle(j => j.Name == job.Name);
    }

    [Fact]
    public async Task should_gracefully_handle_cancellation()
    {
        // given
        _storage
            .AcquireDueJobsAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ct = callInfo.ArgAt<CancellationToken>(2);
                ct.ThrowIfCancellationRequested();
                return _EmptyJobs;
            });

        var sut = _CreateService();

        // when — cancel immediately
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var executeTask = sut.StartAsync(cts.Token);

        // then — should complete without throwing
        await executeTask;
    }

    [Fact]
    public async Task should_cancel_job_when_timeout_exceeded()
    {
        // given
        var job = _CreateRecurringJob();
        job.Timeout = TimeSpan.FromMilliseconds(50);
        _SetupAcquireOnce(job);

        _dispatcher.DelayDuration = TimeSpan.FromMilliseconds(200);
        var sut = _CreateService();

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then
        await _storage
            .Received()
            .UpdateExecutionAsync(
                Arg.Is<JobExecution>(e => e.Status == JobExecutionStatus.Failed && e.Error!.Contains("timed out")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_default_timeout_when_job_timeout_is_null()
    {
        // given
        var job = _CreateRecurringJob();
        job.Timeout = null;
        _SetupAcquireOnce(job);

        _dispatcher.DelayDuration = TimeSpan.FromMilliseconds(200);

        var options = Options.Create(
            new SchedulerOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(10),
                DefaultJobTimeout = TimeSpan.FromMilliseconds(50),
            }
        );

        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var sut = new SchedulerBackgroundService(
            _storage,
            _dispatcher,
            _cronCache,
            _timeProvider,
            _logger,
            options,
            sp
        );

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then
        await _storage
            .Received()
            .UpdateExecutionAsync(
                Arg.Is<JobExecution>(e => e.Status == JobExecutionStatus.Failed && e.Error!.Contains("timed out")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_timeout_when_no_timeout_configured()
    {
        // given
        var job = _CreateRecurringJob();
        job.Timeout = null;
        _SetupAcquireOnce(job);

        _dispatcher.DelayDuration = TimeSpan.FromMilliseconds(100);

        var options = Options.Create(
            new SchedulerOptions { PollingInterval = TimeSpan.FromMilliseconds(10), DefaultJobTimeout = null }
        );

        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var sut = new SchedulerBackgroundService(
            _storage,
            _dispatcher,
            _cronCache,
            _timeProvider,
            _logger,
            options,
            sp
        );

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then
        await _storage
            .Received()
            .UpdateExecutionAsync(
                Arg.Is<JobExecution>(e => e.Status == JobExecutionStatus.Succeeded),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_skip_misfired_recurring_job_when_strategy_is_skip()
    {
        // given
        var job = _CreateRecurringJob();
        job.MisfireStrategy = MisfireStrategy.SkipAndScheduleNext;
        job.NextRunTime = _timeProvider.GetUtcNow().AddMinutes(-5);
        _SetupAcquireOnce(job);

        var options = Options.Create(
            new SchedulerOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(10),
                MisfireThreshold = TimeSpan.FromMinutes(1),
            }
        );

        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var sut = new SchedulerBackgroundService(
            _storage,
            _dispatcher,
            _cronCache,
            _timeProvider,
            _logger,
            options,
            sp
        );

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then — job updated with next cron occurrence, dispatch not called
        _dispatcher.DispatchedJobs.Should().BeEmpty();
        await _storage
            .Received()
            .UpdateJobAsync(
                Arg.Is<ScheduledJob>(j =>
                    j.Status == ScheduledJobStatus.Pending
                    && j.NextRunTime != null
                    && j.NextRunTime > _timeProvider.GetUtcNow()
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_execute_misfired_job_when_strategy_is_fire_immediately()
    {
        // given
        var job = _CreateRecurringJob();
        job.MisfireStrategy = MisfireStrategy.FireImmediately;
        job.NextRunTime = _timeProvider.GetUtcNow().AddMinutes(-5);
        _SetupAcquireOnce(job);

        var options = Options.Create(
            new SchedulerOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(10),
                MisfireThreshold = TimeSpan.FromMinutes(1),
            }
        );

        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var sut = new SchedulerBackgroundService(
            _storage,
            _dispatcher,
            _cronCache,
            _timeProvider,
            _logger,
            options,
            sp
        );

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then — job executed normally
        _dispatcher.DispatchedJobs.Should().ContainSingle(j => j.Name == job.Name);
    }

    [Fact]
    public async Task should_not_check_misfire_for_one_time_jobs()
    {
        // given
        var job = _CreateOneTimeJob();
        job.MisfireStrategy = MisfireStrategy.SkipAndScheduleNext;
        job.NextRunTime = _timeProvider.GetUtcNow().AddMinutes(-5);
        _SetupAcquireOnce(job);

        var options = Options.Create(
            new SchedulerOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(10),
                MisfireThreshold = TimeSpan.FromMinutes(1),
            }
        );

        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var sut = new SchedulerBackgroundService(
            _storage,
            _dispatcher,
            _cronCache,
            _timeProvider,
            _logger,
            options,
            sp
        );

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then — one-time jobs always execute
        _dispatcher.DispatchedJobs.Should().ContainSingle(j => j.Name == job.Name);
    }

    [Fact]
    public async Task should_preserve_disabled_status_when_job_disabled_during_execution()
    {
        // given — job is disabled concurrently while executing
        var job = _CreateRecurringJob();
        _SetupAcquireOnce(job);

        var disabledJob = new ScheduledJob
        {
            Id = job.Id,
            Name = job.Name,
            Type = job.Type,
            CronExpression = job.CronExpression,
            TimeZone = job.TimeZone,
            Status = ScheduledJobStatus.Disabled,
            IsEnabled = false,
            NextRunTime = null,
            RetryCount = 0,
            SkipIfRunning = false,
            DateCreated = job.DateCreated,
            DateUpdated = job.DateUpdated,
            MisfireStrategy = job.MisfireStrategy,
        };

        _storage.GetJobByNameAsync(job.Name, Arg.Any<CancellationToken>()).Returns(disabledJob);

        var sut = _CreateService();

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then — disabled state preserved, not overwritten to Pending
        await _storage
            .Received()
            .UpdateJobAsync(
                Arg.Is<ScheduledJob>(j =>
                    j.Status == ScheduledJobStatus.Disabled && !j.IsEnabled && j.NextRunTime == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_preserve_disabled_status_when_job_disabled_during_failed_execution()
    {
        // given — job fails and was disabled concurrently
        var job = _CreateRecurringJob();
        job.RetryIntervals = [1000];
        _SetupAcquireOnce(job);
        _dispatcher.ExceptionToThrow = new InvalidOperationException("fail");

        var disabledJob = new ScheduledJob
        {
            Id = job.Id,
            Name = job.Name,
            Type = job.Type,
            CronExpression = job.CronExpression,
            TimeZone = job.TimeZone,
            Status = ScheduledJobStatus.Disabled,
            IsEnabled = false,
            NextRunTime = null,
            RetryCount = 0,
            SkipIfRunning = false,
            DateCreated = job.DateCreated,
            DateUpdated = job.DateUpdated,
            MisfireStrategy = job.MisfireStrategy,
        };

        _storage.GetJobByNameAsync(job.Name, Arg.Any<CancellationToken>()).Returns(disabledJob);

        var sut = _CreateService();

        // when
        using var cts = new CancellationTokenSource();
        await _RunOneIterationAsync(sut, cts);

        // then — disabled state preserved, retry not scheduled
        await _storage
            .Received()
            .UpdateJobAsync(
                Arg.Is<ScheduledJob>(j =>
                    j.Status == ScheduledJobStatus.Disabled && !j.IsEnabled && j.NextRunTime == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    // -- helpers --

    private void _SetupAcquireOnce(ScheduledJob job)
    {
        var callCount = 0;
        _storage
            .AcquireDueJobsAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    return (IReadOnlyList<ScheduledJob>)[job];
                }

                return _EmptyJobs;
            });
    }

    private SchedulerBackgroundService _CreateService(IDistributedLockProvider? lockProvider = null)
    {
        var services = new ServiceCollection();

        if (lockProvider is not null)
        {
            services.AddSingleton(lockProvider);
        }

        var sp = services.BuildServiceProvider();

        return new SchedulerBackgroundService(_storage, _dispatcher, _cronCache, _timeProvider, _logger, _options, sp);
    }

    private static async Task _RunOneIterationAsync(SchedulerBackgroundService sut, CancellationTokenSource cts)
    {
        // Start the service, give it time to poll once, then cancel
        _ = sut.StartAsync(cts.Token);
        await Task.Delay(300, CancellationToken.None);
        await cts.CancelAsync();

        try
        {
            await sut.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    private ScheduledJob _CreateRecurringJob()
    {
        var now = _timeProvider.GetUtcNow();
        return new ScheduledJob
        {
            Id = Guid.NewGuid(),
            Name = Faker.Lorem.Word() + "-job",
            Type = ScheduledJobType.Recurring,
            CronExpression = "*/5 * * * * *",
            TimeZone = "UTC",
            Status = ScheduledJobStatus.Running,
            NextRunTime = now,
            RetryCount = 0,
            SkipIfRunning = false,
            IsEnabled = true,
            DateCreated = now,
            DateUpdated = now,
            MisfireStrategy = MisfireStrategy.FireImmediately,
        };
    }

    private ScheduledJob _CreateOneTimeJob()
    {
        var now = _timeProvider.GetUtcNow();
        return new ScheduledJob
        {
            Id = Guid.NewGuid(),
            Name = Faker.Lorem.Word() + "-onetime",
            Type = ScheduledJobType.OneTime,
            CronExpression = null,
            TimeZone = "UTC",
            Status = ScheduledJobStatus.Running,
            NextRunTime = now,
            RetryCount = 0,
            SkipIfRunning = false,
            IsEnabled = true,
            DateCreated = now,
            DateUpdated = now,
            MisfireStrategy = MisfireStrategy.FireImmediately,
        };
    }

    /// <summary>
    /// Manual test double for IScheduledJobDispatcher (internal interface cannot be proxied by NSubstitute).
    /// </summary>
    private sealed class StubDispatcher : IScheduledJobDispatcher
    {
        public List<ScheduledJob> DispatchedJobs { get; } = [];
        public Exception? ExceptionToThrow { get; set; }
        public TimeSpan DelayDuration { get; set; }

        public async Task DispatchAsync(
            ScheduledJob job,
            JobExecution execution,
            CancellationToken cancellationToken = default
        )
        {
            DispatchedJobs.Add(job);

            if (DelayDuration > TimeSpan.Zero)
            {
                await Task.Delay(DelayDuration, cancellationToken).ConfigureAwait(false);
            }

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }
        }
    }
}
