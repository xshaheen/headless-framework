// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Testing.Tests;
using Headless.Messaging;
using Headless.Messaging.Messages;

namespace Tests;

public sealed class ConsumeFilterTests : TestBase
{
    [Fact]
    public async Task should_return_completed_task_for_default_executing()
    {
        // given
        var filter = new TestConsumeFilter();
        var context = _CreateExecutingContext();

        // when
        var task = filter.OnSubscribeExecutingAsync(context);

        // then
        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [Fact]
    public async Task should_return_completed_task_for_default_executed()
    {
        // given
        var filter = new TestConsumeFilter();
        var context = _CreateExecutedContext();

        // when
        var task = filter.OnSubscribeExecutedAsync(context);

        // then
        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [Fact]
    public async Task should_return_completed_task_for_default_exception()
    {
        // given
        var filter = new TestConsumeFilter();
        var context = _CreateExceptionContext();

        // when
        var task = filter.OnSubscribeExceptionAsync(context);

        // then
        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [Fact]
    public async Task should_allow_override_of_executing()
    {
        // given
        var filter = new CustomConsumeFilter();
        var context = _CreateExecutingContext();

        // when
        await filter.OnSubscribeExecutingAsync(context);

        // then
        filter.ExecutingCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_allow_override_of_executed()
    {
        // given
        var filter = new CustomConsumeFilter();
        var context = _CreateExecutedContext();

        // when
        await filter.OnSubscribeExecutedAsync(context);

        // then
        filter.ExecutedCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_allow_override_of_exception()
    {
        // given
        var filter = new CustomConsumeFilter();
        var context = _CreateExceptionContext();

        // when
        await filter.OnSubscribeExceptionAsync(context);

        // then
        filter.ExceptionCalled.Should().BeTrue();
    }

    [Fact]
    public void should_create_executing_context_with_arguments()
    {
        // given
        var consumerContext = _CreateConsumerContext();
        var arguments = new object?[] { "arg1", 42, null };

        // when
        var context = new ExecutingContext(consumerContext, arguments);

        // then
        context.Arguments.Should().BeEquivalentTo(arguments);
    }

    [Fact]
    public void should_create_executed_context_with_result()
    {
        // given
        var consumerContext = _CreateConsumerContext();
        var result = new { Success = true, Value = 42 };

        // when
        var context = new ExecutedContext(consumerContext, result);

        // then
        context.Result.Should().Be(result);
    }

    [Fact]
    public void should_create_exception_context_with_exception()
    {
        // given
        var consumerContext = _CreateConsumerContext();
        var exception = new InvalidOperationException("Test error");

        // when
        var context = new ExceptionContext(consumerContext, exception);

        // then
        context.Exception.Should().BeSameAs(exception);
        context.ExceptionHandled.Should().BeFalse();
        context.Result.Should().BeNull();
    }

    [Fact]
    public void should_create_exception_context_with_handled_flag()
    {
        // given
        var consumerContext = _CreateConsumerContext();
        var exception = new InvalidOperationException("Test error");

        // when
        var context = new ExceptionContext(consumerContext, exception)
        {
            ExceptionHandled = true,
            Result = "recovered",
        };

        // then
        context.ExceptionHandled.Should().BeTrue();
        context.Result.Should().Be("recovered");
    }

    [Fact]
    public void should_inherit_consumer_context_in_filter_context()
    {
        // given
        var consumerContext = _CreateConsumerContext();

        // when
        var executingContext = new ExecutingContext(consumerContext, []);
        var executedContext = new ExecutedContext(consumerContext, null);
        var exceptionContext = new ExceptionContext(consumerContext, new InvalidOperationException("Test"));

        // then
        executingContext.ConsumerDescriptor.Should().Be(consumerContext.ConsumerDescriptor);
        executingContext.MediumMessage.Should().Be(consumerContext.MediumMessage);

        executedContext.ConsumerDescriptor.Should().Be(consumerContext.ConsumerDescriptor);
        executedContext.MediumMessage.Should().Be(consumerContext.MediumMessage);

        exceptionContext.ConsumerDescriptor.Should().Be(consumerContext.ConsumerDescriptor);
        exceptionContext.MediumMessage.Should().Be(consumerContext.MediumMessage);
    }

    private ConsumerContext _CreateConsumerContext()
    {
        var methodInfo = typeof(TestConsumeFilter).GetMethod(
            nameof(TestConsumeFilter.OnSubscribeExecutingAsync),
            BindingFlags.Public | BindingFlags.Instance,
            [typeof(ExecutingContext)]
        )!;

        var descriptor = new ConsumerExecutorDescriptor
        {
            MethodInfo = methodInfo,
            ImplTypeInfo = typeof(TestConsumeFilter).GetTypeInfo(),
            TopicName = "test.topic",
            GroupName = "test-group",
        };

        var message = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = Faker.Random.Guid().ToString(),
                [Headers.MessageName] = "test.topic",
            },
            new FilterTestMessage("test")
        );

        var mediumMessage = new MediumMessage
        {
            DbId = Faker.Random.Guid().ToString(),
            Origin = message,
            Content = "{}",
            Added = DateTime.UtcNow,
        };

        return new ConsumerContext(descriptor, mediumMessage);
    }

    private ExecutingContext _CreateExecutingContext()
    {
        return new ExecutingContext(_CreateConsumerContext(), []);
    }

    private ExecutedContext _CreateExecutedContext()
    {
        return new ExecutedContext(_CreateConsumerContext(), null);
    }

    private ExceptionContext _CreateExceptionContext()
    {
        return new ExceptionContext(_CreateConsumerContext(), new InvalidOperationException("Test error"));
    }
}

// Filter test message
public sealed record FilterTestMessage(string Value);

// Test implementation using default behavior
public sealed class TestConsumeFilter : ConsumeFilter;

// Custom implementation with overrides
public sealed class CustomConsumeFilter : ConsumeFilter
{
    public bool ExecutingCalled { get; private set; }
    public bool ExecutedCalled { get; private set; }
    public bool ExceptionCalled { get; private set; }

    public override ValueTask OnSubscribeExecutingAsync(ExecutingContext context)
    {
        ExecutingCalled = true;
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnSubscribeExecutedAsync(ExecutedContext context)
    {
        ExecutedCalled = true;
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnSubscribeExceptionAsync(ExceptionContext context)
    {
        ExceptionCalled = true;
        return ValueTask.CompletedTask;
    }
}
