// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Tests covering multi-filter chain registration and pipeline ordering for <see cref="IConsumeFilter"/>.
/// Drives <see cref="ConsumeExecutionPipeline"/> directly with a real <see cref="IServiceProvider"/>
/// and a substituted <see cref="IMessageDispatcher"/>.
/// </summary>
public sealed class ConsumeFilterPipelineTests : TestBase
{
    [Fact]
    public void should_register_multiple_subscribe_filters_via_enumerable_resolution()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);

        // when
        builder.AddSubscribeFilter<RecordingFilterA>().AddSubscribeFilter<RecordingFilterB>();
        services.AddSingleton<FilterCallRecorder>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetServices<IConsumeFilter>().ToArray();

        // then
        resolved.Should().HaveCount(2);
        resolved.Select(f => f.GetType()).Should().Equal(typeof(RecordingFilterA), typeof(RecordingFilterB));
    }

    [Fact]
    public void should_be_idempotent_when_same_filter_type_is_registered_twice()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);

        // when
        builder.AddSubscribeFilter<RecordingFilterA>().AddSubscribeFilter<RecordingFilterA>();
        services.AddSingleton<FilterCallRecorder>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetServices<IConsumeFilter>().ToArray();

        // then
        resolved.Should().HaveCount(1);
        resolved.Single().Should().BeOfType<RecordingFilterA>();
    }

    [Fact]
    public async Task should_invoke_executing_phase_in_registration_order_then_consumer_then_executed_phase_in_reverse_order()
    {
        // given
        var recorder = new FilterCallRecorder();
        var services = _BuildServiceCollection(recorder);
        new MessagingBuilder(services).AddSubscribeFilter<RecordingFilterA>().AddSubscribeFilter<RecordingFilterB>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(),
            new SimpleMessage("hi"),
            typeof(SimpleMessage),
            AbortToken
        );

        // then
        recorder.Calls.Should().Equal("A.executing", "B.executing", "dispatcher", "B.executed", "A.executed");
    }

    [Fact]
    public async Task should_expose_resolved_tenant_id_on_executing_context()
    {
        // given
        var recorder = new FilterCallRecorder();
        var services = _BuildServiceCollection(recorder);
        new MessagingBuilder(services).AddSubscribeFilter<TenantObservingFilter>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(tenantId: "acme"),
            new SimpleMessage("hi"),
            typeof(SimpleMessage),
            AbortToken
        );

        // then
        recorder.Calls.Should().Contain("tenant=acme");
    }

    [Fact]
    public async Task should_invoke_exception_phase_in_reverse_order_when_dispatcher_throws()
    {
        // given
        var recorder = new FilterCallRecorder();
        var services = _BuildServiceCollection(recorder, dispatcherShouldThrow: true);
        new MessagingBuilder(services).AddSubscribeFilter<RecordingFilterA>().AddSubscribeFilter<RecordingFilterB>();
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync(
                _BuildConsumerContext(),
                new SimpleMessage("hi"),
                typeof(SimpleMessage),
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("dispatcher boom");
        recorder.Calls.Should().Equal("A.executing", "B.executing", "dispatcher.throw", "B.exception", "A.exception");
    }

    [Fact]
    public async Task should_swallow_exception_when_any_filter_sets_exception_handled_true()
    {
        // given
        var recorder = new FilterCallRecorder();
        var services = _BuildServiceCollection(recorder, dispatcherShouldThrow: true);
        new MessagingBuilder(services).AddSubscribeFilter<HandlingFilter>().AddSubscribeFilter<RecordingFilterB>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(),
            new SimpleMessage("hi"),
            typeof(SimpleMessage),
            AbortToken
        );

        // then — both exception filters ran; outer Handling filter swallowed the exception
        recorder.Calls.Should().Contain("dispatcher.throw");
        recorder.Calls.Should().Contain("B.exception");
        recorder.Calls.Should().Contain("Handling.exception(handled=true)");
    }

    [Fact]
    public async Task should_only_invoke_exception_phase_for_filters_whose_executing_phase_completed()
    {
        // given: A enters executing fine, B throws during executing, C never enters
        var recorder = new FilterCallRecorder();
        var services = _BuildServiceCollection(recorder);
        new MessagingBuilder(services)
            .AddSubscribeFilter<RecordingFilterA>()
            .AddSubscribeFilter<ExecutingThrowingFilter>()
            .AddSubscribeFilter<RecordingFilterB>();
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync(
                _BuildConsumerContext(),
                new SimpleMessage("hi"),
                typeof(SimpleMessage),
                AbortToken
            );

        // then — A and ExecutingThrow ran executing; only those two appear in the exception phase.
        // B never entered executing, so its exception phase MUST NOT run (otherwise filters might dispose
        // state they never initialized, mirroring the ASP.NET MVC stack-unwind contract).
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("filter executing boom");
        recorder
            .Calls.Should()
            .Equal("A.executing", "ExecutingThrow.executing", "ExecutingThrow.exception", "A.exception");
        recorder.Calls.Should().NotContain("B.executing");
        recorder.Calls.Should().NotContain("B.exception");
    }

    [Fact]
    public async Task should_propagate_executed_result_mutation_from_inner_filter_to_outer_filter()
    {
        // given
        var recorder = new FilterCallRecorder();
        var services = _BuildServiceCollection(recorder);
        new MessagingBuilder(services)
            .AddSubscribeFilter<OuterResultObservingFilter>()
            .AddSubscribeFilter<InnerResultMutatingFilter>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(),
            new SimpleMessage("hi"),
            typeof(SimpleMessage),
            AbortToken
        );

        // then — inner runs first in reverse, sets Result; outer (last in reverse) observes the mutation
        recorder.Calls.Should().Contain("Inner.executed.set-result");
        recorder.Calls.Should().Contain("Outer.executed.observed=mutated-by-inner");
    }

    [Fact]
    public async Task should_preserve_handled_exception_result_back_to_resultObj()
    {
        // given — pipeline throws; handling filter sets ExceptionHandled=true AND a fallback Result
        var recorder = new FilterCallRecorder();
        var services = _BuildServiceCollection(recorder, dispatcherShouldThrow: true);
        new MessagingBuilder(services).AddSubscribeFilter<HandlingFilterWithFallback>();
        var pipeline = _BuildPipeline(services);

        // when
        var result = await pipeline.ExecuteAsync(
            _BuildConsumerContext(),
            new SimpleMessage("hi"),
            typeof(SimpleMessage),
            AbortToken
        );

        // then — exception was swallowed; fallback Result surfaces in ConsumerExecutedResult
        result.Result.Should().Be("fallback-from-handler");
    }

    [Fact]
    public async Task should_throw_when_no_filters_are_registered_and_dispatcher_throws()
    {
        // given
        var recorder = new FilterCallRecorder();
        var services = _BuildServiceCollection(recorder, dispatcherShouldThrow: true);
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync(
                _BuildConsumerContext(),
                new SimpleMessage("hi"),
                typeof(SimpleMessage),
                AbortToken
            );

        // then — zero-filter case rethrows directly
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("dispatcher boom");
    }

    [Fact]
    public async Task should_suppress_OCE_from_post_success_consume_filter()
    {
        // given — pins F2 (consume side): post-success OCE thrown by a filter must be treated like
        // any other post-success failure (logged + suppressed). The consumer body already committed,
        // so surfacing an after-success OCE would trigger a spurious transport retry.
        var recorder = new FilterCallRecorder();
        var services = _BuildServiceCollection(recorder);
        new MessagingBuilder(services).AddSubscribeFilter<ExecutedOceThrowingFilter>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(),
            new SimpleMessage("hi"),
            typeof(SimpleMessage),
            AbortToken
        );

        // then — the OCE is suppressed; caller (and transport) does not see a faulted consume
        recorder.Calls.Should().Contain("dispatcher");
        recorder.Calls.Should().Contain("ExecutedOce.executed.throw");
    }

    [Fact]
    public async Task should_propagate_OCE_when_exception_handled_set_to_true()
    {
        // given — pins F7: cancellation must always reach the host. A consume filter cannot
        // silently swallow OperationCanceledException by setting ExceptionHandled=true.
        var services = new ServiceCollection();
        var recorder = new FilterCallRecorder();
        services.AddSingleton(recorder);
        services.AddSingleton<IMessageDispatcher>(new OceThrowingDispatcher(recorder));
        new MessagingBuilder(services).AddSubscribeFilter<HandlingFilter>();
        var pipeline = _BuildPipeline(services);

        // when — dispatcher throws OCE; HandlingFilter sets ExceptionHandled = true
        var act = async () =>
            await pipeline.ExecuteAsync(
                _BuildConsumerContext(),
                new SimpleMessage("hi"),
                typeof(SimpleMessage),
                AbortToken
            );

        // then — OCE propagates despite ExceptionHandled = true
        await act.Should().ThrowAsync<OperationCanceledException>();
        recorder.Calls.Should().Contain("dispatcher.oce.throw");
    }

    private static ServiceCollection _BuildServiceCollection(
        FilterCallRecorder recorder,
        bool dispatcherShouldThrow = false
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddSingleton<IMessageDispatcher>(new RecordingMessageDispatcher(recorder, dispatcherShouldThrow));
        return services;
    }

    private static ConsumeExecutionPipeline _BuildPipeline(ServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        var registry = Substitute.For<IRuntimeConsumerRegistry>();
        return new ConsumeExecutionPipeline(provider, registry);
    }

    private static ConsumerContext _BuildConsumerContext(string? tenantId = null)
    {
        var descriptor = new ConsumerExecutorDescriptor
        {
            MethodInfo = typeof(ConsumeFilterPipelineTests).GetMethod(
                nameof(_BuildConsumerContext),
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
                binder: null,
                types: [typeof(string)],
                modifiers: null
            )!,
            ImplTypeInfo = typeof(ConsumeFilterPipelineTests).GetTypeInfo(),
            TopicName = "test.topic",
            GroupName = "test-group",
            // HandlerId left default (null) so the pipeline takes the dispatcher branch.
        };

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "msg-1",
            [Headers.MessageName] = "test.topic",
        };

        if (tenantId is not null)
        {
            headers[Headers.TenantId] = tenantId;
        }

        var origin = new Message(headers, new SimpleMessage("payload"));

        var medium = new MediumMessage
        {
            StorageId = 1,
            Origin = origin,
            Content = "{}",
            Added = DateTime.UtcNow,
        };

        return new ConsumerContext(descriptor, medium);
    }
}

internal sealed record SimpleMessage(string Value);

internal sealed class FilterCallRecorder
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _calls = new();

    public IReadOnlyList<string> Calls => _calls.ToArray();

    public void Record(string call) => _calls.Enqueue(call);
}

internal sealed class RecordingFilterA(FilterCallRecorder recorder) : ConsumeFilter
{
    public override ValueTask OnSubscribeExecutingAsync(
        ExecutingContext context,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Record("A.executing");
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnSubscribeExecutedAsync(
        ExecutedContext context,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Record("A.executed");
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnSubscribeExceptionAsync(
        ExceptionContext context,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Record("A.exception");
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingFilterB(FilterCallRecorder recorder) : ConsumeFilter
{
    public override ValueTask OnSubscribeExecutingAsync(
        ExecutingContext context,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Record("B.executing");
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnSubscribeExecutedAsync(
        ExecutedContext context,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Record("B.executed");
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnSubscribeExceptionAsync(
        ExceptionContext context,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Record("B.exception");
        return ValueTask.CompletedTask;
    }
}

internal sealed class TenantObservingFilter(FilterCallRecorder recorder) : ConsumeFilter
{
    public override ValueTask OnSubscribeExecutingAsync(
        ExecutingContext context,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Record($"tenant={context.TenantId ?? "<null>"}");
        return ValueTask.CompletedTask;
    }
}

internal sealed class HandlingFilter(FilterCallRecorder recorder) : ConsumeFilter
{
    public override ValueTask OnSubscribeExceptionAsync(
        ExceptionContext context,
        CancellationToken cancellationToken = default
    )
    {
        context.ExceptionHandled = true;
        recorder.Record($"Handling.exception(handled={context.ExceptionHandled.ToString().ToLowerInvariant()})");
        return ValueTask.CompletedTask;
    }
}

internal sealed class ExecutingThrowingFilter(FilterCallRecorder recorder) : ConsumeFilter
{
    public override ValueTask OnSubscribeExecutingAsync(
        ExecutingContext context,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Record("ExecutingThrow.executing");
        throw new InvalidOperationException("filter executing boom");
    }

    public override ValueTask OnSubscribeExceptionAsync(
        ExceptionContext context,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Record("ExecutingThrow.exception");
        return ValueTask.CompletedTask;
    }
}

internal sealed class InnerResultMutatingFilter(FilterCallRecorder recorder) : ConsumeFilter
{
    public override ValueTask OnSubscribeExecutedAsync(
        ExecutedContext context,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Record("Inner.executed.set-result");
        context.Result = "mutated-by-inner";
        return ValueTask.CompletedTask;
    }
}

internal sealed class OuterResultObservingFilter(FilterCallRecorder recorder) : ConsumeFilter
{
    public override ValueTask OnSubscribeExecutedAsync(
        ExecutedContext context,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Record($"Outer.executed.observed={context.Result ?? "<null>"}");
        return ValueTask.CompletedTask;
    }
}

internal sealed class HandlingFilterWithFallback(FilterCallRecorder recorder) : ConsumeFilter
{
    public override ValueTask OnSubscribeExceptionAsync(
        ExceptionContext context,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Record("HandlingWithFallback.exception");
        context.ExceptionHandled = true;
        context.Result = "fallback-from-handler";
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingMessageDispatcher(FilterCallRecorder recorder, bool shouldThrow) : IMessageDispatcher
{
    public Task DispatchAsync<TMessage>(ConsumeContext<TMessage> context, CancellationToken cancellationToken)
        where TMessage : class
    {
        return DispatchInScopeAsync(serviceProvider: null!, context, cancellationToken);
    }

    public Task DispatchInScopeAsync<TMessage>(
        IServiceProvider serviceProvider,
        ConsumeContext<TMessage> context,
        CancellationToken cancellationToken
    )
        where TMessage : class
    {
        return _Run();
    }

    public Task DispatchInScopeAsync<TMessage>(
        IServiceProvider serviceProvider,
        ConsumerExecutorDescriptor descriptor,
        ConsumeContext<TMessage> context,
        CancellationToken cancellationToken
    )
        where TMessage : class
    {
        return _Run();
    }

    private Task _Run()
    {
        // Return a faulted task instead of throwing synchronously — the pipeline calls the dispatcher
        // via reflection (`MethodInfo.Invoke`) and a synchronous throw would be wrapped in
        // TargetInvocationException, masking the original exception type.
        if (shouldThrow)
        {
            recorder.Record("dispatcher.throw");
            return Task.FromException(new InvalidOperationException("dispatcher boom"));
        }

        recorder.Record("dispatcher");
        return Task.CompletedTask;
    }
}

internal sealed class ExecutedOceThrowingFilter(FilterCallRecorder recorder) : ConsumeFilter
{
    public override ValueTask OnSubscribeExecutedAsync(
        ExecutedContext context,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Record("ExecutedOce.executed.throw");
        throw new OperationCanceledException("post-success cancellation should be suppressed");
    }
}

internal sealed class OceThrowingDispatcher(FilterCallRecorder recorder) : IMessageDispatcher
{
    public Task DispatchAsync<TMessage>(ConsumeContext<TMessage> context, CancellationToken cancellationToken)
        where TMessage : class
    {
        return DispatchInScopeAsync(serviceProvider: null!, context, cancellationToken);
    }

    public Task DispatchInScopeAsync<TMessage>(
        IServiceProvider serviceProvider,
        ConsumeContext<TMessage> context,
        CancellationToken cancellationToken
    )
        where TMessage : class => _Run();

    public Task DispatchInScopeAsync<TMessage>(
        IServiceProvider serviceProvider,
        ConsumerExecutorDescriptor descriptor,
        ConsumeContext<TMessage> context,
        CancellationToken cancellationToken
    )
        where TMessage : class => _Run();

    private Task _Run()
    {
        recorder.Record("dispatcher.oce.throw");
        return Task.FromException(new OperationCanceledException("dispatcher cancelled"));
    }
}
