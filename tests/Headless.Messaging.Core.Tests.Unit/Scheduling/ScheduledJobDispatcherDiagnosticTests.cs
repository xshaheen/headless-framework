// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Scheduling;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Scheduling;

public sealed class ScheduledJobDispatcherDiagnosticTests : TestBase
{
    private readonly List<KeyValuePair<string, object?>> _events = [];
    private readonly IDisposable? _subscription;

    public ScheduledJobDispatcherDiagnosticTests()
    {
        _subscription = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver(_events));
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _subscription?.Dispose();
        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_emit_before_and_after_events_when_dispatch_succeeds()
    {
        // given
        var handler = Substitute.For<IConsume<ScheduledTrigger>>();
        var (sut, job, execution) = _CreateSut(handler);

        // when
        await sut.DispatchAsync(job, execution, AbortToken);

        // then
        _events.Should().HaveCount(2);

        var beforeEvent = _events[0];
        beforeEvent.Key.Should().Be(MessageDiagnosticListenerNames.BeforeScheduledJobDispatch);
        var beforeData = beforeEvent.Value.Should().BeOfType<ScheduledJobEventData>().Subject;
        beforeData.JobName.Should().Be(job.Name);
        beforeData.ExecutionId.Should().Be(execution.Id);
        beforeData.Attempt.Should().Be(execution.RetryAttempt + 1);
        beforeData.ScheduledTime.Should().Be(execution.ScheduledTime);
        beforeData.OperationTimestamp.Should().NotBeNull();

        var afterEvent = _events[1];
        afterEvent.Key.Should().Be(MessageDiagnosticListenerNames.AfterScheduledJobDispatch);
        var afterData = afterEvent.Value.Should().BeOfType<ScheduledJobEventData>().Subject;
        afterData.JobName.Should().Be(job.Name);
        afterData.ExecutionId.Should().Be(execution.Id);
        afterData.Attempt.Should().Be(execution.RetryAttempt + 1);
        afterData.ElapsedTimeMs.Should().NotBeNull();
        afterData.ElapsedTimeMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task should_emit_before_and_error_events_when_dispatch_fails()
    {
        // given
        var expectedException = new InvalidOperationException("handler failure");
        var handler = Substitute.For<IConsume<ScheduledTrigger>>();
        handler
            .Consume(Arg.Any<ConsumeContext<ScheduledTrigger>>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask>(_ => throw expectedException);

        var (sut, job, execution) = _CreateSut(handler);

        // when
        var act = () => sut.DispatchAsync(job, execution, AbortToken);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // then
        _events.Should().HaveCount(2);

        var beforeEvent = _events[0];
        beforeEvent.Key.Should().Be(MessageDiagnosticListenerNames.BeforeScheduledJobDispatch);

        var errorEvent = _events[1];
        errorEvent.Key.Should().Be(MessageDiagnosticListenerNames.ErrorScheduledJobDispatch);
        var errorData = errorEvent.Value.Should().BeOfType<ScheduledJobEventData>().Subject;
        errorData.JobName.Should().Be(job.Name);
        errorData.ExecutionId.Should().Be(execution.Id);
        errorData.Attempt.Should().Be(execution.RetryAttempt + 1);
        errorData.Exception.Should().BeSameAs(expectedException);
        errorData.ElapsedTimeMs.Should().NotBeNull();
        errorData.ElapsedTimeMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task should_not_throw_when_no_diagnostic_subscriber()
    {
        // given — dispose the subscription so no listener is active
        _subscription?.Dispose();

        var handler = Substitute.For<IConsume<ScheduledTrigger>>();
        var (sut, job, execution) = _CreateSut(handler);

        // when
        await sut.DispatchAsync(job, execution, AbortToken);

        // then — dispatch succeeds without errors
        await handler.Received(1).Consume(Arg.Any<ConsumeContext<ScheduledTrigger>>(), AbortToken);
    }

    // -- helpers --

    private (ScheduledJobDispatcher Sut, ScheduledJob Job, JobExecution Execution) _CreateSut(
        IConsume<ScheduledTrigger> handler
    )
    {
        var now = DateTimeOffset.UtcNow;
        var jobName = Faker.Lorem.Word() + "-diag-job";

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IConsume<ScheduledTrigger>>(jobName, handler);
        var sp = services.BuildServiceProvider();

        var sut = new ScheduledJobDispatcher(sp.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        var job = new ScheduledJob
        {
            Id = Guid.NewGuid(),
            Name = jobName,
            Type = ScheduledJobType.Recurring,
            CronExpression = "*/5 * * * *",
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
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            ScheduledTime = now,
            StartedAt = now,
            Status = JobExecutionStatus.Running,
            RetryAttempt = 0,
        };

        return (sut, job, execution);
    }

    // -- diagnostic observer helpers --

    private sealed class ListenerObserver(List<KeyValuePair<string, object?>> events) : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == MessageDiagnosticListenerNames.DiagnosticListenerName)
            {
                value.Subscribe(new EventObserver(events));
            }
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }
    }

    private sealed class EventObserver(List<KeyValuePair<string, object?>> events)
        : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value) => events.Add(value);

        public void OnCompleted() { }

        public void OnError(Exception error) { }
    }
}
