// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;

namespace Tests;

public sealed class JobSchedulerExtensionsTests : TestBase
{
    private static readonly Request _Request = new();
    private static readonly JobFunctionDescriptor _Descriptor = new("requestless", null, "", JobPriority.Normal, 0);
    private static readonly DateTimeOffset _ExecutionTime = new(2026, 9, 5, 18, 0, 0, TimeSpan.FromHours(3));
    private static readonly TimeSpan _Delay = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(0, nameof(IJobScheduler.EnqueueAsync), true)]
    [InlineData(1, nameof(IJobScheduler.EnqueueAsync), false)]
    [InlineData(2, nameof(IJobScheduler.ScheduleAsync), true)]
    [InlineData(3, nameof(IJobScheduler.ScheduleAsync), false)]
    [InlineData(4, nameof(IJobScheduler.ScheduleAfterAsync), true)]
    [InlineData(5, nameof(IJobScheduler.ScheduleAfterAsync), false)]
    public async Task fluent_calls_forward_once_to_the_matching_options_overload(
        int operation,
        string methodName,
        bool typed
    )
    {
        var expected = Task.FromResult(Guid.NewGuid());
        var scheduler = _CreateScheduler(expected);
        var callbackCount = 0;
        JobOptionsBuilder? captured = null;

        var actual = _Invoke(
            operation,
            scheduler,
            options =>
            {
                callbackCount++;
                captured = options;
                options
                    .WithRetries(0)
                    .WithRetryIntervals(2, 5)
                    .WithNodeDeathPolicy(NodeDeathPolicy.Skip)
                    .RequireAtomicEnlistment()
                    .WithCorrelationId("correlation")
                    .WithCausationId("cause")
                    .WithDescription("invocation")
                    .WithTenantId("tenant")
                    .AsSystemJob();
            },
            AbortToken
        );

        actual.Should().BeSameAs(expected);
        (await actual).Should().Be(await expected);
        callbackCount.Should().Be(1);
        captured!.WithRetries(9).WithRetryIntervals(99);
        var call = scheduler.ReceivedCalls().Should().ContainSingle().Which;
        call.GetMethodInfo().Name.Should().Be(methodName);
        call.GetMethodInfo().IsGenericMethod.Should().Be(typed);
        var arguments = call.GetArguments();
        arguments[0].Should().BeSameAs(typed ? _Request : (object)_Descriptor);
        arguments[^1].Should().Be(AbortToken);
        var options = arguments[^2].Should().BeOfType<JobOptions>().Which;
        options
            .Should()
            .BeEquivalentTo(
                new JobOptions
                {
                    Retries = 0,
                    RetryIntervals = [2, 5],
                    OnNodeDeath = NodeDeathPolicy.Skip,
                    RequireAtomicEnlistment = true,
                    CorrelationId = "correlation",
                    CausationId = "cause",
                    Description = "invocation",
                    TenantId = "tenant",
                    IsSystemJob = true,
                }
            );
        if (methodName == nameof(IJobScheduler.ScheduleAsync))
        {
            arguments[1].Should().Be(_ExecutionTime);
        }
        else if (methodName == nameof(IJobScheduler.ScheduleAfterAsync))
        {
            arguments[1].Should().Be(_Delay);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void invalid_adapter_inputs_and_callback_failures_never_call_the_scheduler(int operation)
    {
        var scheduler = _CreateScheduler(Task.FromResult(Guid.NewGuid()));
        var callbackCount = 0;
        Action<JobOptionsBuilder> configure = _ => callbackCount++;
        Action nullReceiver = () => _ = _Invoke(operation, null!, configure, AbortToken);
        nullReceiver.Should().Throw<ArgumentNullException>().WithParameterName("scheduler");
        callbackCount.Should().Be(0);
        Action nullCallback = () => _ = _Invoke(operation, scheduler, null!, AbortToken);
        nullCallback.Should().Throw<ArgumentNullException>().WithParameterName("configure");
        var failure = new InvalidOperationException("callback failed");
        Action throwingCallback = () => _ = _Invoke(operation, scheduler, _ => throw failure, AbortToken);
        throwingCallback.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        scheduler.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task forwarded_failure_and_cancellation_keep_the_original_task_and_outcome(int operation)
    {
        var failure = new InvalidOperationException("scheduler failed");
        var faulted = Task.FromException<Guid>(failure);
        var scheduler = _CreateScheduler(faulted);
        var actual = _Invoke(operation, scheduler, _ => { }, AbortToken);
        actual.Should().BeSameAs(faulted);
        Func<Task> failed = () => actual;
        (await failed.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var canceled = Task.FromCanceled<Guid>(cancellation.Token);
        scheduler = _CreateScheduler(canceled);
        var callbackCount = 0;
        actual = _Invoke(operation, scheduler, _ => callbackCount++, cancellation.Token);
        actual.Should().BeSameAs(canceled);
        callbackCount.Should().Be(1);
        scheduler.ReceivedCalls().Should().ContainSingle().Which.GetArguments()[^1].Should().Be(cancellation.Token);
        Func<Task> canceledCall = () => actual;
        (await canceledCall.Should().ThrowAsync<OperationCanceledException>())
            .Which.CancellationToken.Should()
            .Be(cancellation.Token);
    }

    [Fact]
    public async Task callbacks_receive_fresh_builders_and_documented_call_forms_compile()
    {
        var scheduler = _CreateScheduler(Task.FromResult(Guid.NewGuid()));
        JobOptionsBuilder? first = null;
        JobOptionsBuilder? second = null;
        await scheduler.EnqueueAsync(_Request, options => first = options, AbortToken);
        await scheduler.EnqueueAsync(
            _Descriptor,
            configure: options =>
            {
                second = options;
                options.WithRetries(0);
            },
            cancellationToken: AbortToken
        );
        first.Should().NotBeSameAs(second);
        await scheduler.EnqueueAsync(_Request, AbortToken);
        await scheduler.EnqueueAsync(_Descriptor, cancellationToken: default);
        await scheduler.EnqueueAsync(_Request, options: null, AbortToken);
        await scheduler.EnqueueAsync(_Descriptor, null, AbortToken);
        await scheduler.EnqueueAsync(_Request, default, AbortToken);
        await scheduler.EnqueueAsync(_Request, new JobOptions(), AbortToken);
    }

    private static Task<Guid> _Invoke(
        int operation,
        IJobScheduler scheduler,
        Action<JobOptionsBuilder> configure,
        CancellationToken cancellationToken
    ) =>
        operation switch
        {
            0 => scheduler.EnqueueAsync(_Request, configure, cancellationToken),
            1 => scheduler.EnqueueAsync(_Descriptor, configure, cancellationToken),
            2 => scheduler.ScheduleAsync(_Request, _ExecutionTime, configure, cancellationToken),
            3 => scheduler.ScheduleAsync(_Descriptor, _ExecutionTime, configure, cancellationToken),
            4 => scheduler.ScheduleAfterAsync(_Request, _Delay, configure, cancellationToken),
            5 => scheduler.ScheduleAfterAsync(_Descriptor, _Delay, configure, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    private static IJobScheduler _CreateScheduler(Task<Guid> result)
    {
        var scheduler = Substitute.For<IJobScheduler>();
        scheduler.EnqueueAsync(_Request, Arg.Any<JobOptions>(), Arg.Any<CancellationToken>()).Returns(result);
        scheduler.EnqueueAsync(_Descriptor, Arg.Any<JobOptions>(), Arg.Any<CancellationToken>()).Returns(result);
        scheduler
            .ScheduleAsync(_Request, _ExecutionTime, Arg.Any<JobOptions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        scheduler
            .ScheduleAsync(_Descriptor, _ExecutionTime, Arg.Any<JobOptions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        scheduler
            .ScheduleAfterAsync(_Request, _Delay, Arg.Any<JobOptions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        scheduler
            .ScheduleAfterAsync(_Descriptor, _Delay, Arg.Any<JobOptions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        scheduler.ClearReceivedCalls();
        return scheduler;
    }

    private sealed record Request;
}
