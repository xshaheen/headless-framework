// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Managers;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Managers;

/// <summary>
/// A committed delete tells open dashboards which rows went away, so every client drops them instead of showing them
/// until its next reload; a failed notification never turns the committed delete into an error.
/// </summary>
public sealed class JobsManagerRemovalNotificationTests : TestBase
{
    private readonly List<JobsPostCommitSignalService> _signals = [];

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var signals in _signals)
        {
            signals.Dispose();
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_notify_dashboards_after_a_time_job_delete()
    {
        var sut = _Create();
        var id = Guid.NewGuid();
        sut.Persistence.RemoveTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns(1);

        var result = await sut.TimeJobs.DeleteAsync(id, AbortToken);

        result.IsSucceeded.Should().BeTrue();
        await sut.Notification.Received(1).RemoveTimeJobNotifyAsync(id);
    }

    [Fact]
    public async Task should_notify_every_requested_id_after_a_time_job_batch_delete()
    {
        var sut = _Create();
        List<Guid> ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        sut.Persistence.RemoveTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns(ids.Count);

        await sut.TimeJobs.DeleteBatchAsync(ids, AbortToken);

        foreach (var id in ids)
        {
            await sut.Notification.Received(1).RemoveTimeJobNotifyAsync(id);
        }
    }

    [Fact]
    public async Task should_notify_dashboards_after_a_cron_job_delete()
    {
        var sut = _Create();
        var id = Guid.NewGuid();
        sut.Persistence.RemoveCronJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns(1);

        var result = await sut.CronJobs.DeleteAsync(id, AbortToken);

        result.IsSucceeded.Should().BeTrue();
        await sut.Notification.Received(1).RemoveCronJobNotifyAsync(id);
    }

    [Fact]
    public async Task should_not_notify_when_nothing_was_deleted()
    {
        var sut = _Create();
        sut.Persistence.RemoveTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns(0);
        sut.Persistence.RemoveCronJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns(0);

        await sut.TimeJobs.DeleteAsync(Guid.NewGuid(), AbortToken);
        await sut.CronJobs.DeleteAsync(Guid.NewGuid(), AbortToken);

        await sut.Notification.DidNotReceive().RemoveTimeJobNotifyAsync(Arg.Any<Guid>());
        await sut.Notification.DidNotReceive().RemoveCronJobNotifyAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task should_log_and_keep_the_delete_when_the_notification_fails()
    {
        var sut = _Create();
        var id = Guid.NewGuid();
        var boom = new InvalidOperationException("hub unavailable");
        sut.Persistence.RemoveTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns(1);
        sut.Notification.RemoveTimeJobNotifyAsync(id).Returns(Task.FromException(boom));

        var result = await sut.TimeJobs.DeleteAsync(id, AbortToken);

        result.IsSucceeded.Should().BeTrue();
        result.AffectedRows.Should().Be(1);
        sut.Logger.Entries.Should().ContainSingle(x => x.EventId == 3239 && x.Exception == boom);
    }

    private Sut _Create()
    {
        var persistence = Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var notification = Substitute.For<IJobsNotificationHubSender>();
        var logger = new CapturingLogger<JobsManager<TimeJobEntity, CronJobEntity>>();
#pragma warning disable CA2000 // False positive: _signals owns it and DisposeAsyncCore disposes it.
        var signals = new JobsPostCommitSignalService(
            TestActivationBarrier.Opened(),
            TimeProvider.System,
            NullLogger<JobsPostCommitSignalService>.Instance
        );
#pragma warning restore CA2000
        _signals.Add(signals);
        var manager = new JobsManager<TimeJobEntity, CronJobEntity>(
            persistence,
            Substitute.For<IJobsHostScheduler>(),
            TimeProvider.System,
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            notification,
            new JobsExecutionContext(),
            Substitute.For<IJobsDispatcher>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            signals,
            JobFunctionRegistryBuilder.Build([], [], []),
            logger
        );

        var facade = new JobsManagerFacade<TimeJobEntity, CronJobEntity>(manager);

        return new Sut(persistence, notification, logger, facade, facade);
    }

    private sealed record Sut(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> Persistence,
        IJobsNotificationHubSender Notification,
        CapturingLogger<JobsManager<TimeJobEntity, CronJobEntity>> Logger,
        ITimeJobManager<TimeJobEntity> TimeJobs,
        ICronJobManager<CronJobEntity> CronJobs
    );

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, int EventId, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add((logLevel, eventId.Id, exception));
        }
    }
}
