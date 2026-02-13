// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Scheduling;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Scheduling;

public sealed class ScheduledJobManagerTests : TestBase
{
    private readonly IScheduledJobStorage _storage = Substitute.For<IScheduledJobStorage>();
    private readonly CronScheduleCache _cronCache = new();

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly ScheduledJobManager _sut;

    public ScheduledJobManagerTests()
    {
        _sut = new ScheduledJobManager(_storage, _cronCache, _timeProvider);
    }

    [Fact]
    public async Task should_return_all_jobs()
    {
        // given
        IReadOnlyList<ScheduledJob> expected = [_CreateJob("job-a"), _CreateJob("job-b")];
        _storage.GetAllJobsAsync(Arg.Any<CancellationToken>()).Returns(expected);

        // when
        var result = await _sut.GetAllAsync(AbortToken);

        // then
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task should_list_recent_executions_by_job_name()
    {
        // given
        var job = _CreateJob("job-a");
        IReadOnlyList<JobExecution> executions =
        [
            new()
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                ScheduledTime = _timeProvider.GetUtcNow(),
                Status = JobExecutionStatus.Succeeded,
                RetryAttempt = 0,
            },
            new()
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                ScheduledTime = _timeProvider.GetUtcNow().AddMinutes(-1),
                Status = JobExecutionStatus.Succeeded,
                RetryAttempt = 0,
            },
        ];

        _storage.GetJobByNameAsync(job.Name, Arg.Any<CancellationToken>()).Returns(job);
        _storage.GetExecutionsAsync(job.Id, 2, Arg.Any<CancellationToken>()).Returns(executions);

        // when
        var result = await _sut.ListExecutionsAsync(job.Name, limit: 2, cancellationToken: AbortToken);

        // then
        result.Should().BeSameAs(executions);
    }

    [Fact]
    public async Task should_return_job_by_name()
    {
        // given
        var job = _CreateJob("my-job");
        _storage.GetJobByNameAsync("my-job", Arg.Any<CancellationToken>()).Returns(job);

        // when
        var result = await _sut.GetByNameAsync("my-job", AbortToken);

        // then
        result.Should().BeSameAs(job);
    }

    [Fact]
    public async Task should_enable_disabled_job_and_recompute_next_run_time()
    {
        // given
        var job = _CreateJob("daily-job");
        job.Status = ScheduledJobStatus.Disabled;
        job.IsEnabled = false;
        job.NextRunTime = null;
        job.CronExpression = "0 0 0 * * *";

        _storage.GetJobByNameAsync("daily-job", Arg.Any<CancellationToken>()).Returns(job);

        // when
        var result = await _sut.EnableAsync("daily-job", AbortToken);

        // then
        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(ScheduledJobStatus.Pending);
        job.IsEnabled.Should().BeTrue();
        job.NextRunTime.Should().NotBeNull();
        job.DateUpdated.Should().Be(_timeProvider.GetUtcNow());
        await _storage.Received(1).UpdateJobAsync(job, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_disable_enabled_job()
    {
        // given
        var job = _CreateJob("daily-job");
        _storage.GetJobByNameAsync("daily-job", Arg.Any<CancellationToken>()).Returns(job);

        // when
        var result = await _sut.DisableAsync("daily-job", AbortToken);

        // then
        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(ScheduledJobStatus.Disabled);
        job.IsEnabled.Should().BeFalse();
        job.NextRunTime.Should().BeNull();
        job.DateUpdated.Should().Be(_timeProvider.GetUtcNow());
        await _storage.Received(1).UpdateJobAsync(job, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_trigger_immediate_execution()
    {
        // given
        var job = _CreateJob("on-demand-job");
        _storage.GetJobByNameAsync("on-demand-job", Arg.Any<CancellationToken>()).Returns(job);

        // when
        var result = await _sut.TriggerAsync("on-demand-job", AbortToken);

        // then
        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(ScheduledJobStatus.Pending);
        job.NextRunTime.Should().Be(_timeProvider.GetUtcNow());
        await _storage.Received(1).UpdateJobAsync(job, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_delete_job_by_name()
    {
        // given
        var job = _CreateJob("deletable-job");
        _storage.GetJobByNameAsync("deletable-job", Arg.Any<CancellationToken>()).Returns(job);

        // when
        var result = await _sut.DeleteAsync("deletable-job", AbortToken);

        // then
        result.IsSuccess.Should().BeTrue();
        await _storage.Received(1).DeleteJobAsync(job.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_return_not_found_when_job_missing_for_enable()
    {
        // given
        _storage.GetJobByNameAsync("nonexistent", Arg.Any<CancellationToken>()).Returns((ScheduledJob?)null);

        // when
        var result = await _sut.EnableAsync("nonexistent", AbortToken);

        // then
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>().Which.Key.Should().Be("nonexistent");
    }

    [Fact]
    public async Task should_return_not_found_when_job_missing_for_disable()
    {
        // given
        _storage.GetJobByNameAsync("nonexistent", Arg.Any<CancellationToken>()).Returns((ScheduledJob?)null);

        // when
        var result = await _sut.DisableAsync("nonexistent", AbortToken);

        // then
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>().Which.Key.Should().Be("nonexistent");
    }

    [Fact]
    public async Task should_return_not_found_when_job_missing_for_trigger()
    {
        // given
        _storage.GetJobByNameAsync("nonexistent", Arg.Any<CancellationToken>()).Returns((ScheduledJob?)null);

        // when
        var result = await _sut.TriggerAsync("nonexistent", AbortToken);

        // then
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>().Which.Key.Should().Be("nonexistent");
    }

    [Fact]
    public async Task should_return_not_found_when_job_missing_for_delete()
    {
        // given
        _storage.GetJobByNameAsync("nonexistent", Arg.Any<CancellationToken>()).Returns((ScheduledJob?)null);

        // when
        var result = await _sut.DeleteAsync("nonexistent", AbortToken);

        // then
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>().Which.Key.Should().Be("nonexistent");
    }

    [Fact]
    public async Task should_create_one_time_job_with_correct_properties()
    {
        // given
        var runAt = _timeProvider.GetUtcNow().AddHours(2);
        var consumerType = typeof(object);
        const string payload = """{"data":"test"}""";

        // when
        await _sut.ScheduleOnceAsync("onetime-job", runAt, consumerType, payload, AbortToken);

        // then
        await _storage
            .Received(1)
            .UpsertJobAsync(
                Arg.Is<ScheduledJob>(j =>
                    j.Name == "onetime-job"
                    && j.Type == ScheduledJobType.OneTime
                    && j.NextRunTime == runAt
                    && j.Payload == payload
                    && j.Status == ScheduledJobStatus.Pending
                    && j.IsEnabled
                    && j.MisfireStrategy == MisfireStrategy.FireImmediately
                    && j.ConsumerTypeName == consumerType.AssemblyQualifiedName
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_schedule_one_time_job_with_generic_consumer_and_json_payload()
    {
        // given
        var runAt = _timeProvider.GetUtcNow().AddMinutes(30);
        var payload = new SamplePayload("hello", 2);

        // when
        await _sut.ScheduleOnceAsync<SampleScheduledConsumer, SamplePayload>(
            "generic-job",
            runAt,
            payload,
            AbortToken
        );

        // then
        await _storage
            .Received(1)
            .UpsertJobAsync(
                Arg.Is<ScheduledJob>(j =>
                    j.Name == "generic-job"
                    && j.ConsumerTypeName == typeof(SampleScheduledConsumer).AssemblyQualifiedName
                    && j.Payload == """{"Name":"hello","Count":2}"""
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_schedule_one_time_job_with_generic_string_payload_without_json_quoting()
    {
        // given
        var runAt = _timeProvider.GetUtcNow().AddMinutes(30);

        // when
        await _sut.ScheduleOnceAsync<SampleScheduledConsumer, string>(
            "generic-string-job",
            runAt,
            "hello",
            AbortToken
        );

        // then
        await _storage
            .Received(1)
            .UpsertJobAsync(
                Arg.Is<ScheduledJob>(j =>
                    j.Name == "generic-string-job"
                    && j.ConsumerTypeName == typeof(SampleScheduledConsumer).AssemblyQualifiedName
                    && j.Payload == "hello"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_throw_when_run_at_is_in_the_past()
    {
        // given
        var pastTime = _timeProvider.GetUtcNow().AddHours(-1);
        var consumerType = typeof(object);

        // when
        var act = () => _sut.ScheduleOnceAsync("past-job", pastTime, consumerType, null, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*future*");
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

    private sealed record SamplePayload(string Name, int Count);

    private sealed class SampleScheduledConsumer : IConsume<ScheduledTrigger>
    {
        public ValueTask Consume(ConsumeContext<ScheduledTrigger> context, CancellationToken cancellationToken)
        {
            _ = context;
            return ValueTask.CompletedTask;
        }
    }
}
