// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Domain;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable MA0015 // Specify the parameter name in ArgumentException
namespace Tests;

public sealed class ServiceProviderLocalEventBusTests : TestBase
{
    #region Test Infrastructure

    private sealed record TestLocalMessage(string Value) : IDomainEvent
    {
        public string UniqueId => Value;
    }

    private sealed class TrackingHandler : IDomainEventHandler<TestLocalMessage>
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

    [DomainEventHandlerOrder(-10)]
    private sealed class OrderedHandlerNegative10(List<string> invocationOrder) : IDomainEventHandler<TestLocalMessage>
    {
        public List<string> InvocationOrder { get; } = invocationOrder;

        public ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            InvocationOrder.Add("Negative10");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderedHandlerDefault(List<string> invocationOrder) : IDomainEventHandler<TestLocalMessage>
    {
        public List<string> InvocationOrder { get; } = invocationOrder;

        public ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            InvocationOrder.Add("Default0");
            return ValueTask.CompletedTask;
        }
    }

    [DomainEventHandlerOrder(10)]
    private sealed class OrderedHandlerPositive10(List<string> invocationOrder) : IDomainEventHandler<TestLocalMessage>
    {
        public List<string> InvocationOrder { get; } = invocationOrder;

        public ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            InvocationOrder.Add("Positive10");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingHandler(string exceptionMessage = "Handler failed")
        : IDomainEventHandler<TestLocalMessage>
    {
        public bool WasInvoked { get; private set; }

        public string ExceptionMessage { get; } = exceptionMessage;

        public ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            throw new InvalidOperationException(ExceptionMessage);
        }
    }

    private sealed class FailingAndCancellingHandler(
        CancellationTokenSource cts,
        string exceptionMessage = "Handler failed"
    ) : IDomainEventHandler<TestLocalMessage>
    {
        public async ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            throw new InvalidOperationException(exceptionMessage);
        }
    }

    private sealed class TargetInvocationExceptionHandler : IDomainEventHandler<TestLocalMessage>
    {
        public ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            throw new TargetInvocationException(new ArgumentException("Inner exception message"));
        }
    }

    private sealed class TrackingAfterFailureHandler : IDomainEventHandler<TestLocalMessage>
    {
        public bool WasInvoked { get; private set; }

        public ValueTask HandleAsync(TestLocalMessage message, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            return ValueTask.CompletedTask;
        }
    }

    private static ServiceProviderLocalEventBus _CreatePublisher(IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        return new ServiceProviderLocalEventBus(serviceProvider);
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
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(handler1);
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(handler2);
        var publisher = _CreatePublisher(services);
        var message = new TestLocalMessage("test-value");

        // when
        await publisher.PublishAsync(message, AbortToken);

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
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(new OrderedHandlerPositive10(invocationOrder));
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(new OrderedHandlerNegative10(invocationOrder));
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(new OrderedHandlerDefault(invocationOrder));
        var publisher = _CreatePublisher(services);

        // when
        await publisher.PublishAsync(new TestLocalMessage("test"), AbortToken);

        // then
        invocationOrder.Should().ContainInOrder("Negative10", "Default0", "Positive10");
    }

    [Fact]
    public async Task should_invoke_handlers_with_default_order_zero_async()
    {
        // given
        var invocationOrder = new List<string>();
        var services = new ServiceCollection();
        // Two handlers where one keeps the default order (0) - explicit orders still win
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(new OrderedHandlerDefault(invocationOrder));
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(new OrderedHandlerNegative10(invocationOrder));
        var publisher = _CreatePublisher(services);

        // when
        await publisher.PublishAsync(new TestLocalMessage("test"), AbortToken);

        // then - Negative10 (-10) should come before Default0 (0)
        invocationOrder.Should().ContainInOrder("Negative10", "Default0");
    }

    [Fact]
    public async Task should_unwrap_target_invocation_exception_async()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(new TargetInvocationExceptionHandler());
        var publisher = _CreatePublisher(services);

        // when
        var act = async () => await publisher.PublishAsync(new TestLocalMessage("test"), AbortToken);

        // then - inner exception should be thrown, not TargetInvocationException
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("Inner exception message");
    }

    [Fact]
    public async Task should_pass_cancellation_token_to_handlers()
    {
        // given
        var handler = new TrackingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(handler);
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
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(failingHandler);
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
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(failingHandler1);
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(failingHandler2);
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
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(failingHandler);
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(trackingHandler);
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

    [Fact]
    public async Task should_throw_when_cancellation_requested_before_handler_invocation()
    {
        // given — a pre-cancelled token must short-circuit the handler loop before any handler runs.
        var handler = new TrackingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(handler);
        var publisher = _CreatePublisher(services);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await publisher.PublishAsync(new TestLocalMessage("test"), cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.ReceivedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_preserve_prior_handler_exceptions_when_cancellation_is_requested_async()
    {
        // given
        using var cts = new CancellationTokenSource();
        var failingHandler = new FailingAndCancellingHandler(cts, "Initial failure");
        var trackingHandler = new TrackingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(failingHandler);
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(trackingHandler);
        var publisher = _CreatePublisher(services);

        // when
        var act = async () => await publisher.PublishAsync(new TestLocalMessage("test"), cts.Token);

        // then
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.WithMessage("Initial failure");
        trackingHandler.ReceivedMessages.Should().BeEmpty();
    }

    #endregion

    #region Non-generic runtime-typed dispatch (invoker cache)

    [Fact]
    public async Task should_dispatch_to_runtime_type_handlers_via_non_generic_publish_async()
    {
        // given — referenced through IDomainEvent so the non-generic PublishAsync(IDomainEvent) overload
        // (compiled invoker) is selected. Handlers fire only if it routes by runtime type to
        // PublishAsync<TestLocalMessage>; a wrong IDomainEvent-typed dispatch would resolve zero handlers.
        var handler = new TrackingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(handler);
        var publisher = _CreatePublisher(services);
        IDomainEvent message = new TestLocalMessage("runtime-typed-async");
        using var cts = new CancellationTokenSource();

        // when
        await publisher.PublishAsync(message, cts.Token);

        // then
        handler.ReceivedMessages.Should().ContainSingle().Which.Should().Be("runtime-typed-async");
        handler.ReceivedTokens.Should().ContainSingle().Which.Should().Be(cts.Token);
    }

    [Fact]
    public async Task should_cache_and_reuse_runtime_typed_invoker_across_calls()
    {
        // given
        var handler = new TrackingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(handler);
        var publisher = _CreatePublisher(services);

        // when — repeated non-generic dispatch of the same runtime type reuses the cached invoker
        IDomainEvent first = new TestLocalMessage("first");
        IDomainEvent second = new TestLocalMessage("second");
        await publisher.PublishAsync(first, AbortToken);
        await publisher.PublishAsync(second, AbortToken);

        // then
        handler.ReceivedMessages.Should().ContainInOrder("first", "second");
    }

    [Fact]
    public async Task should_propagate_handler_failure_through_non_generic_publish()
    {
        // given — the generic path's exception semantics must flow through the compiled invoker.
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(new FailingHandler("non-generic failure"));
        var publisher = _CreatePublisher(services);
        IDomainEvent message = new TestLocalMessage("test");

        // when
        var act = async () => await publisher.PublishAsync(message, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("non-generic failure");
    }

    #endregion

    #region Handler Order Caching Tests

    [Fact]
    public async Task should_cache_handler_order()
    {
        // given
        var invocationOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(new OrderedHandlerPositive10(invocationOrder));
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(new OrderedHandlerNegative10(invocationOrder));
        var publisher = _CreatePublisher(services);

        // when - publish multiple times
        await publisher.PublishAsync(new TestLocalMessage("test1"), AbortToken);
        invocationOrder.Clear();
        await publisher.PublishAsync(new TestLocalMessage("test2"), AbortToken);

        // then - order should be consistent (cached)
        invocationOrder.Should().ContainInOrder("Negative10", "Positive10");
    }

    [Fact]
    public async Task should_resolve_order_only_once_per_type()
    {
        // given - use same publisher instance for multiple calls
        var invocationOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(new OrderedHandlerPositive10(invocationOrder));
        services.AddSingleton<IDomainEventHandler<TestLocalMessage>>(new OrderedHandlerDefault(invocationOrder));
        var publisher = _CreatePublisher(services);

        // when - publish repeatedly against the same cached order
        await publisher.PublishAsync(new TestLocalMessage("first"), AbortToken);
        await publisher.PublishAsync(new TestLocalMessage("second"), AbortToken);
        await publisher.PublishAsync(new TestLocalMessage("third"), AbortToken);

        // then - all should maintain same order (cache is shared)
        // Expected: Default0, Positive10 repeated 3 times
        invocationOrder.Should().HaveCount(6);
        invocationOrder
            .Should()
            .ContainInOrder("Default0", "Positive10", "Default0", "Positive10", "Default0", "Positive10");
    }

    #endregion
}
