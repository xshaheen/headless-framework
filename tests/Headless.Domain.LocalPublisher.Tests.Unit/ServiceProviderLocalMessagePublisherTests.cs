// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class ServiceProviderLocalMessagePublisherTests
{
    #region Test Infrastructure

    private sealed record TestLocalMessage(string Value) : ILocalMessage
    {
        public string UniqueId => Value;
    }

    private sealed class TrackingHandler : ILocalMessageHandler<TestLocalMessage>
    {
        public List<string> ReceivedMessages { get; } = [];
        public List<CancellationToken> ReceivedTokens { get; } = [];

        public ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            ReceivedMessages.Add(message.Value);
            ReceivedTokens.Add(cancellationToken);
            return ValueTask.CompletedTask;
        }
    }

    [LocalEventHandlerOrder(-10)]
    private sealed class OrderedHandlerNegative10(List<string> invocationOrder) : ILocalMessageHandler<TestLocalMessage>
    {
        public List<string> InvocationOrder { get; } = invocationOrder;

        public ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            InvocationOrder.Add("Negative10");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderedHandlerDefault(List<string> invocationOrder) : ILocalMessageHandler<TestLocalMessage>
    {
        public List<string> InvocationOrder { get; } = invocationOrder;

        public ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            InvocationOrder.Add("Default0");
            return ValueTask.CompletedTask;
        }
    }

    [LocalEventHandlerOrder(10)]
    private sealed class OrderedHandlerPositive10(List<string> invocationOrder) : ILocalMessageHandler<TestLocalMessage>
    {
        public List<string> InvocationOrder { get; } = invocationOrder;

        public ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            InvocationOrder.Add("Positive10");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingHandler(string exceptionMessage = "Handler failed")
        : ILocalMessageHandler<TestLocalMessage>
    {
        public bool WasInvoked { get; private set; }

        public string ExceptionMessage { get; } = exceptionMessage;

        public ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            throw new InvalidOperationException(ExceptionMessage);
        }
    }

    private sealed class TargetInvocationExceptionHandler : ILocalMessageHandler<TestLocalMessage>
    {
        public ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            throw new TargetInvocationException(new ArgumentException("Inner exception message"));
        }
    }

    private sealed class TrackingAfterFailureHandler : ILocalMessageHandler<TestLocalMessage>
    {
        public bool WasInvoked { get; private set; }

        public ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            return ValueTask.CompletedTask;
        }
    }

    private static ServiceProviderLocalMessagePublisher _CreatePublisher(IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        return new ServiceProviderLocalMessagePublisher(serviceProvider);
    }

    #endregion

    #region Publish (Synchronous) Tests

    [Fact]
    public void should_invoke_all_handlers()
    {
        // given
        var handler1 = new TrackingHandler();
        var handler2 = new TrackingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(handler1);
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(handler2);
        var publisher = _CreatePublisher(services);
        var message = new TestLocalMessage("test-value");

        // when
        publisher.Publish(message);

        // then
        handler1.ReceivedMessages.Should().ContainSingle().Which.Should().Be("test-value");
        handler2.ReceivedMessages.Should().ContainSingle().Which.Should().Be("test-value");
    }

    [Fact]
    public void should_invoke_handlers_in_order_attribute_order()
    {
        // given
        var invocationOrder = new List<string>();
        var services = new ServiceCollection();
        // Register in wrong order intentionally
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(new OrderedHandlerPositive10(invocationOrder));
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(new OrderedHandlerNegative10(invocationOrder));
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(new OrderedHandlerDefault(invocationOrder));
        var publisher = _CreatePublisher(services);

        // when
        publisher.Publish(new TestLocalMessage("test"));

        // then - should be ordered: -10, 0, 10
        invocationOrder.Should().ContainInOrder("Negative10", "Default0", "Positive10");
    }

    [Fact]
    public void should_invoke_handlers_with_default_order_zero()
    {
        // given
        var invocationOrder = new List<string>();
        var services = new ServiceCollection();
        // Two handlers with default order (0) - should maintain registration order
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(new OrderedHandlerDefault(invocationOrder));
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(new OrderedHandlerNegative10(invocationOrder));
        var publisher = _CreatePublisher(services);

        // when
        publisher.Publish(new TestLocalMessage("test"));

        // then - Negative10 (-10) should come before Default0 (0)
        invocationOrder.Should().ContainInOrder("Negative10", "Default0");
    }

    [Fact]
    public void should_pass_message_to_handler()
    {
        // given
        var handler = new TrackingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(handler);
        var publisher = _CreatePublisher(services);
        var message = new TestLocalMessage("expected-value");

        // when
        publisher.Publish(message);

        // then
        handler.ReceivedMessages.Should().ContainSingle().Which.Should().Be("expected-value");
    }

    [Fact]
    public void should_throw_single_exception_when_one_handler_fails()
    {
        // given
        var failingHandler = new FailingHandler("Single failure");
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(failingHandler);
        var publisher = _CreatePublisher(services);

        // when
        var act = () => publisher.Publish(new TestLocalMessage("test"));

        // then - single exception should be re-thrown directly, not wrapped
        act.Should().Throw<InvalidOperationException>().WithMessage("Single failure");
    }

    [Fact]
    public void should_throw_aggregate_exception_when_multiple_handlers_fail()
    {
        // given
        var failingHandler1 = new FailingHandler("Failure 1");
        var failingHandler2 = new FailingHandler("Failure 2");
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(failingHandler1);
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(failingHandler2);
        var publisher = _CreatePublisher(services);

        // when
        var act = () => publisher.Publish(new TestLocalMessage("test"));

        // then
        var exception = act.Should().Throw<AggregateException>().Which;
        exception.InnerExceptions.Should().HaveCount(2);
        exception.InnerExceptions.Should().AllBeOfType<InvalidOperationException>();
        exception.InnerExceptions.Select(e => e.Message).Should().Contain("Failure 1", "Failure 2");
    }

    [Fact]
    public void should_continue_invoking_handlers_after_failure()
    {
        // given
        var failingHandler = new FailingHandler();
        var trackingHandler = new TrackingAfterFailureHandler();
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(failingHandler);
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(trackingHandler);
        var publisher = _CreatePublisher(services);

        // when
        var act = () => publisher.Publish(new TestLocalMessage("test"));

        // then - exception thrown but both handlers were invoked
        act.Should().Throw<Exception>();
        failingHandler.WasInvoked.Should().BeTrue();
        trackingHandler.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public void should_unwrap_target_invocation_exception()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(new TargetInvocationExceptionHandler());
        var publisher = _CreatePublisher(services);

        // when
        var act = () => publisher.Publish(new TestLocalMessage("test"));

        // then - inner exception should be thrown, not TargetInvocationException
        act.Should().Throw<ArgumentException>().WithMessage("Inner exception message");
    }

    [Fact]
    public void should_handle_no_registered_handlers()
    {
        // given
        var services = new ServiceCollection();
        var publisher = _CreatePublisher(services);

        // when
        var act = () => publisher.Publish(new TestLocalMessage("test"));

        // then - no exception
        act.Should().NotThrow();
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task should_invoke_all_handlers_async()
    {
        // given
        var handler1 = new TrackingHandler();
        var handler2 = new TrackingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(handler1);
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(handler2);
        var publisher = _CreatePublisher(services);
        var message = new TestLocalMessage("test-value");

        // when
        await publisher.PublishAsync(message, TestContext.Current.CancellationToken);

        // then
        handler1.ReceivedMessages.Should().ContainSingle().Which.Should().Be("test-value");
        handler2.ReceivedMessages.Should().ContainSingle().Which.Should().Be("test-value");
    }

    [Fact]
    public async Task should_invoke_handlers_in_order_async()
    {
        // given
        var invocationOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(new OrderedHandlerPositive10(invocationOrder));
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(new OrderedHandlerNegative10(invocationOrder));
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(new OrderedHandlerDefault(invocationOrder));
        var publisher = _CreatePublisher(services);

        // when
        await publisher.PublishAsync(new TestLocalMessage("test"), TestContext.Current.CancellationToken);

        // then
        invocationOrder.Should().ContainInOrder("Negative10", "Default0", "Positive10");
    }

    [Fact]
    public async Task should_pass_cancellation_token_to_handlers()
    {
        // given
        var handler = new TrackingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(handler);
        var publisher = _CreatePublisher(services);
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // when
        await publisher.PublishAsync(new TestLocalMessage("test"), token);

        // then
        handler.ReceivedTokens.Should().ContainSingle().Which.Should().Be(token);
    }

    [Fact]
    public async Task should_throw_single_exception_when_one_handler_fails_async()
    {
        // given
        var failingHandler = new FailingHandler("Async failure");
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(failingHandler);
        var publisher = _CreatePublisher(services);

        // when
        var act = async () => await publisher.PublishAsync(new TestLocalMessage("test"));

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Async failure");
    }

    [Fact]
    public async Task should_throw_aggregate_exception_when_multiple_handlers_fail_async()
    {
        // given
        var failingHandler1 = new FailingHandler("Async failure 1");
        var failingHandler2 = new FailingHandler("Async failure 2");
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(failingHandler1);
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(failingHandler2);
        var publisher = _CreatePublisher(services);

        // when
        var act = async () => await publisher.PublishAsync(new TestLocalMessage("test"));

        // then
        var exception = (await act.Should().ThrowAsync<AggregateException>()).Which;
        exception.InnerExceptions.Should().HaveCount(2);
        exception.InnerExceptions.Select(e => e.Message).Should().Contain("Async failure 1", "Async failure 2");
    }

    [Fact]
    public async Task should_continue_after_handler_failure_async()
    {
        // given
        var failingHandler = new FailingHandler();
        var trackingHandler = new TrackingAfterFailureHandler();
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(failingHandler);
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(trackingHandler);
        var publisher = _CreatePublisher(services);

        // when
        var act = async () => await publisher.PublishAsync(new TestLocalMessage("test"));

        // then
        await act.Should().ThrowAsync<Exception>();
        failingHandler.WasInvoked.Should().BeTrue();
        trackingHandler.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task should_handle_no_registered_handlers_async()
    {
        // given
        var services = new ServiceCollection();
        var publisher = _CreatePublisher(services);

        // when
        var act = async () => await publisher.PublishAsync(new TestLocalMessage("test"));

        // then
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Handler Order Caching Tests

    [Fact]
    public void should_cache_handler_order()
    {
        // given
        var invocationOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(new OrderedHandlerPositive10(invocationOrder));
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(new OrderedHandlerNegative10(invocationOrder));
        var publisher = _CreatePublisher(services);

        // when - publish multiple times
        publisher.Publish(new TestLocalMessage("test1"));
        invocationOrder.Clear();
        publisher.Publish(new TestLocalMessage("test2"));

        // then - order should be consistent (cached)
        invocationOrder.Should().ContainInOrder("Negative10", "Positive10");
    }

    [Fact]
    public async Task should_resolve_order_only_once_per_type()
    {
        // given - use same publisher instance for multiple calls
        var invocationOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(new OrderedHandlerPositive10(invocationOrder));
        services.AddSingleton<ILocalMessageHandler<TestLocalMessage>>(new OrderedHandlerDefault(invocationOrder));
        var publisher = _CreatePublisher(services);

        // when - mix sync and async calls
        // ReSharper disable once MethodHasAsyncOverload
        publisher.Publish(new TestLocalMessage("sync1"));
        await publisher.PublishAsync(new TestLocalMessage("async1"), TestContext.Current.CancellationToken);
        // ReSharper disable once MethodHasAsyncOverload
        publisher.Publish(new TestLocalMessage("sync2"));

        // then - all should maintain same order (cache is shared)
        // Expected: Default0, Positive10 repeated 3 times
        invocationOrder.Should().HaveCount(6);
        invocationOrder
            .Should()
            .ContainInOrder("Default0", "Positive10", "Default0", "Positive10", "Default0", "Positive10");
    }

    #endregion
}
