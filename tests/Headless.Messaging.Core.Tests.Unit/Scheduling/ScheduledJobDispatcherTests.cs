// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Scheduling;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute.ExceptionExtensions;

namespace Tests.Scheduling;

public sealed class ScheduledJobDispatcherTests : TestBase
{
    [Fact]
    public async Task should_resolve_handler_by_job_name_via_keyed_service()
    {
        // given
        var handler = Substitute.For<IConsume<ScheduledTrigger>>();
        var (sut, job, execution) = _CreateSut(handler);

        // when
        await sut.DispatchAsync(job, execution, AbortToken);

        // then
        await handler.Received(1).Consume(Arg.Any<ConsumeContext<ScheduledTrigger>>(), AbortToken);
    }

    [Fact]
    public async Task should_create_consume_context_with_correct_fields()
    {
        // given
        ConsumeContext<ScheduledTrigger>? capturedContext = null;
        var handler = Substitute.For<IConsume<ScheduledTrigger>>();
        handler
            .Consume(Arg.Any<ConsumeContext<ScheduledTrigger>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedContext = callInfo.ArgAt<ConsumeContext<ScheduledTrigger>>(0);
                return ValueTask.CompletedTask;
            });

        var (sut, job, execution) = _CreateSut(handler);
        job.CronExpression = "*/5 * * * * *";
        job.Payload = """{"key":"value"}""";
        execution.RetryAttempt = 2;

        // when
        await sut.DispatchAsync(job, execution, AbortToken);

        // then
        capturedContext.Should().NotBeNull();
        capturedContext!.MessageId.Should().Be(execution.Id.ToString());
        capturedContext.Topic.Should().Be(job.Name);
        capturedContext.Timestamp.Should().Be(execution.ScheduledTime);
        capturedContext.CorrelationId.Should().BeNull();
        capturedContext.Message.JobName.Should().Be(job.Name);
        capturedContext.Message.ScheduledTime.Should().Be(execution.ScheduledTime);
        capturedContext.Message.Attempt.Should().Be(3); // RetryAttempt + 1
        capturedContext.Message.CronExpression.Should().Be("*/5 * * * * *");
        capturedContext.Message.Payload.Should().Be("""{"key":"value"}""");
        capturedContext.Message.ParentJobId.Should().BeNull();
    }

    [Fact]
    public async Task should_create_scope_per_dispatch()
    {
        // given
        var callCount = 0;
        var handler = Substitute.For<IConsume<ScheduledTrigger>>();
        handler
            .Consume(Arg.Any<ConsumeContext<ScheduledTrigger>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                return ValueTask.CompletedTask;
            });

        var services = new ServiceCollection();
        services.AddKeyedScoped<IConsume<ScheduledTrigger>>("scoped-job", (_, _) => handler);
        var sp = services.BuildServiceProvider();
        var sut = new ScheduledJobDispatcher(sp.GetRequiredService<IServiceScopeFactory>());

        var now = DateTimeOffset.UtcNow;
        var job = _CreateJob("scoped-job", now);
        var execution = _CreateExecution(job, now);

        // when
        await sut.DispatchAsync(job, execution, AbortToken);
        await sut.DispatchAsync(job, execution, AbortToken);

        // then — handler resolved twice (once per scope)
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task should_call_consumer_lifecycle_hooks_when_available()
    {
        // given
        var handler = Substitute.For<IConsume<ScheduledTrigger>, IConsumerLifecycle>();
        var lifecycle = (IConsumerLifecycle)handler;

        var callOrder = new List<string>();
        lifecycle
            .OnStartingAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callOrder.Add("OnStarting");
                return ValueTask.CompletedTask;
            });
        handler
            .Consume(Arg.Any<ConsumeContext<ScheduledTrigger>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callOrder.Add("Consume");
                return ValueTask.CompletedTask;
            });
        lifecycle
            .OnStoppingAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callOrder.Add("OnStopping");
                return ValueTask.CompletedTask;
            });

        var (sut, job, execution) = _CreateSut(handler);

        // when
        await sut.DispatchAsync(job, execution, AbortToken);

        // then
        callOrder.Should().Equal(["OnStarting", "Consume", "OnStopping"]);
    }

    [Fact]
    public async Task should_propagate_handler_exceptions()
    {
        // given
        var handler = Substitute.For<IConsume<ScheduledTrigger>>();
        handler
            .Consume(Arg.Any<ConsumeContext<ScheduledTrigger>>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask>(_ => throw new InvalidOperationException("handler failure"));

        var (sut, job, execution) = _CreateSut(handler);

        // when
        var act = () => sut.DispatchAsync(job, execution, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler failure");
    }

    // -- helpers --

    private (ScheduledJobDispatcher Sut, ScheduledJob Job, JobExecution Execution) _CreateSut(
        IConsume<ScheduledTrigger> handler
    )
    {
        var now = DateTimeOffset.UtcNow;
        var jobName = Faker.Lorem.Word() + "-job";

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IConsume<ScheduledTrigger>>(jobName, handler);
        var sp = services.BuildServiceProvider();

        var sut = new ScheduledJobDispatcher(sp.GetRequiredService<IServiceScopeFactory>());
        var job = _CreateJob(jobName, now);
        var execution = _CreateExecution(job, now);

        return (sut, job, execution);
    }

    private static ScheduledJob _CreateJob(string name, DateTimeOffset now)
    {
        return new ScheduledJob
        {
            Id = Guid.NewGuid(),
            Name = name,
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
        };
    }

    private static JobExecution _CreateExecution(ScheduledJob job, DateTimeOffset now)
    {
        return new JobExecution
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            ScheduledTime = now,
            StartedAt = now,
            Status = JobExecutionStatus.Running,
            RetryAttempt = 0,
        };
    }
}
