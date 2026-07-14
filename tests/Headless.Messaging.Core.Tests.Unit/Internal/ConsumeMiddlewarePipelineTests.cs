// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Internal;

public sealed class ConsumeMiddlewarePipelineTests : TestBase
{
    [Fact]
    public async Task should_invoke_bus_middleware_around_dispatcher()
    {
        // given
        var recorder = new MiddlewareCallRecorder();
        var services = _CreateServices(recorder);
        services.AddScoped<IConsumeMiddleware<ConsumeContext>, RecordingConsumeMiddlewareA>();
        services.AddScoped<IConsumeMiddleware<ConsumeContext>, RecordingConsumeMiddlewareB>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(),
            new MiddlewarePayload("hi"),
            typeof(MiddlewarePayload),
            AbortToken
        );

        // then
        recorder.Calls.Should().Equal("A.before", "B.before", "dispatcher", "B.after", "A.after");
    }

    [Fact]
    public async Task should_log_and_suppress_post_success_consume_middleware_failure()
    {
        // given
        var recorder = new MiddlewareCallRecorder();
        var services = _CreateServices(recorder);
        services.AddScoped<IConsumeMiddleware<ConsumeContext>, PostSuccessThrowingConsumeMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(),
            new MiddlewarePayload("hi"),
            typeof(MiddlewarePayload),
            AbortToken
        );

        // then
        recorder.Calls.Should().Equal("dispatcher", "post-success.throw");
    }

    [Fact]
    public async Task should_rethrow_matching_oce_thrown_after_consumer_completed()
    {
        // given
        using var cts = new CancellationTokenSource();
        var services = _CreateServices(new MiddlewareCallRecorder());
        services.AddSingleton(cts);
        services.AddScoped<IConsumeMiddleware<ConsumeContext>, MatchingOceAfterNextConsumeMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync(
                _BuildConsumerContext(),
                new MiddlewarePayload("hi"),
                typeof(MiddlewarePayload),
                cts.Token
            );

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_convert_pre_success_aggregate_with_matching_oce_to_oce()
    {
        // given
        using var cts = new CancellationTokenSource();
        var services = _CreateServices(new MiddlewareCallRecorder());
        services.AddSingleton(cts);
        services.AddScoped<IConsumeMiddleware<ConsumeContext>, AggregateBeforeNextConsumeMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync(
                _BuildConsumerContext(),
                new MiddlewarePayload("hi"),
                typeof(MiddlewarePayload),
                cts.Token
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_rethrow_when_consume_middleware_swallows_outer_cancellation_and_returns_normally()
    {
        // given
        using var cts = new CancellationTokenSource();
        var recorder = new MiddlewareCallRecorder();
        var services = _CreateServices(recorder);
        services.AddSingleton(cts);
        services.AddScoped<IConsumeMiddleware<ConsumeContext>, SwallowingOuterCancellationConsumeMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync(
                _BuildConsumerContext(),
                new MiddlewarePayload("hi"),
                typeof(MiddlewarePayload),
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ServiceCollection _CreateServices(MiddlewareCallRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddSingleton<IMessageDispatcher>(new RecordingMiddlewareDispatcher(recorder));
        return services;
    }

    private static IConsumeMiddlewarePipeline _BuildPipeline(ServiceCollection services)
    {
        var runtimeRegistry = Substitute.For<IRuntimeConsumerRegistry>();
        return new ConsumeMiddlewarePipeline(services.BuildServiceProvider(), runtimeRegistry);
    }

    private static ConsumerContext _BuildConsumerContext()
    {
        var descriptor = new ConsumerExecutorDescriptor
        {
            IntentType = IntentType.Bus,
            MethodInfo = typeof(ConsumeMiddlewarePipelineTests).GetMethod(
                nameof(_BuildConsumerContext),
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null
            )!,
            ImplTypeInfo = typeof(ConsumeMiddlewarePipelineTests).GetTypeInfo(),
            MessageName = "test.messageName",
            GroupName = "test-group",
        };

        var origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "msg-1",
                [Headers.MessageName] = "test.messageName",
            },
            new MiddlewarePayload("payload")
        );

        return new ConsumerContext(
            descriptor,
            new MediumMessage
            {
                StorageId = Guid.NewGuid(),
                Origin = origin,
                Content = "{}",
                IntentType = IntentType.Bus,
                Added = DateTimeOffset.UtcNow,
            }
        );
    }
}

internal sealed class RecordingConsumeMiddlewareA(MiddlewareCallRecorder recorder) : IConsumeMiddleware<ConsumeContext>
{
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        recorder.Record("A.before");
        await next();
        recorder.Record("A.after");
    }
}

internal sealed class RecordingConsumeMiddlewareB(MiddlewareCallRecorder recorder) : IConsumeMiddleware<ConsumeContext>
{
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        recorder.Record("B.before");
        await next();
        recorder.Record("B.after");
    }
}

internal sealed class PostSuccessThrowingConsumeMiddleware(MiddlewareCallRecorder recorder)
    : IConsumeMiddleware<ConsumeContext>
{
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        await next();
        recorder.Record("post-success.throw");
        throw new ObjectDisposedException(nameof(PostSuccessThrowingConsumeMiddleware));
    }
}

internal sealed class MatchingOceAfterNextConsumeMiddleware(CancellationTokenSource source)
    : IConsumeMiddleware<ConsumeContext>
{
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        await next();
        await source.CancelAsync();
        throw new OperationCanceledException(source.Token);
    }
}

internal sealed class AggregateBeforeNextConsumeMiddleware(CancellationTokenSource source)
    : IConsumeMiddleware<ConsumeContext>
{
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        await source.CancelAsync();
        throw new AggregateException(
            new InvalidOperationException("diagnostic sibling"),
            new OperationCanceledException(source.Token)
        );
    }
}

internal sealed class SwallowingOuterCancellationConsumeMiddleware(CancellationTokenSource source)
    : IConsumeMiddleware<ConsumeContext>
{
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        await source.CancelAsync();
        context.SetCancellationToken(source.Token);

        try
        {
            await next();
        }
        catch (OperationCanceledException)
        {
            // The pipeline must re-check context.CancellationToken after this normal return.
        }
    }
}

internal sealed class RecordingMiddlewareDispatcher(MiddlewareCallRecorder recorder) : IMessageDispatcher
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
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        recorder.Record("dispatcher");
        return Task.CompletedTask;
    }

    public Task DispatchInScopeAsync<TMessage>(
        IServiceProvider serviceProvider,
        ConsumerExecutorDescriptor descriptor,
        ConsumeContext<TMessage> context,
        CancellationToken cancellationToken
    )
        where TMessage : class
    {
        return DispatchInScopeAsync(serviceProvider, context, cancellationToken);
    }
}
