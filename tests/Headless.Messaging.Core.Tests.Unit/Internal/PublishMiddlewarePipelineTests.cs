// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Internal;

public sealed class PublishMiddlewarePipelineTests : TestBase
{
    [Fact]
    public async Task should_invoke_bus_middleware_around_inner_publish()
    {
        // given
        var recorder = new MiddlewareCallRecorder();
        var services = _CreateServices(recorder);
        services.AddScoped<IPublishMiddleware<PublishContext>, RecordingPublishMiddlewareA>();
        services.AddScoped<IPublishMiddleware<PublishContext>, RecordingPublishMiddlewareB>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            new MiddlewarePayload("hi"),
            IntentType.Bus,
            options: null,
            delayTime: null,
            innerPublish: (_, _, _) =>
            {
                recorder.Record("inner");
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        recorder.Calls.Should().Equal("A.before", "B.before", "inner", "B.after", "A.after");
    }

    [Fact]
    public async Task should_short_circuit_publish_when_middleware_does_not_call_next()
    {
        // given
        var recorder = new MiddlewareCallRecorder();
        var services = _CreateServices(recorder);
        services.AddScoped<IPublishMiddleware<PublishContext>, ShortCircuitPublishMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            new MiddlewarePayload("hi"),
            IntentType.Bus,
            options: null,
            delayTime: null,
            innerPublish: (_, _, _) =>
            {
                recorder.Record("inner");
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        recorder.Calls.Should().Equal("short-circuit");
    }

    [Fact]
    public async Task should_log_and_suppress_post_success_publish_middleware_failure()
    {
        // given
        var recorder = new MiddlewareCallRecorder();
        var services = _CreateServices(recorder);
        services.AddScoped<IPublishMiddleware<PublishContext>, PostSuccessThrowingPublishMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            new MiddlewarePayload("hi"),
            IntentType.Bus,
            options: null,
            delayTime: null,
            innerPublish: (_, _, _) =>
            {
                recorder.Record("inner");
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        recorder.Calls.Should().Equal("inner", "post-success.throw");
    }

    [Fact]
    public async Task should_rethrow_matching_oce_thrown_after_inner_publish_completed()
    {
        // given
        using var cts = new CancellationTokenSource();
        var services = _CreateServices(new MiddlewareCallRecorder());
        services.AddSingleton(cts);
        services.AddScoped<IPublishMiddleware<PublishContext>, MatchingOceAfterNextPublishMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync(
                new MiddlewarePayload("hi"),
                IntentType.Bus,
                options: null,
                delayTime: null,
                innerPublish: (_, _, _) => Task.CompletedTask,
                cancellationToken: cts.Token
            );

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_suppress_mixed_aggregate_exception_after_inner_publish_completed()
    {
        // given
        using var cts = new CancellationTokenSource();
        var services = _CreateServices(new MiddlewareCallRecorder());
        services.AddSingleton(cts);
        services.AddScoped<IPublishMiddleware<PublishContext>, MixedAggregateAfterNextPublishMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync(
                new MiddlewarePayload("hi"),
                IntentType.Bus,
                options: null,
                delayTime: null,
                innerPublish: (_, _, _) => Task.CompletedTask,
                cancellationToken: cts.Token
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
        services.AddScoped<IPublishMiddleware<PublishContext>, AggregateBeforeNextPublishMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync(
                new MiddlewarePayload("hi"),
                IntentType.Bus,
                options: null,
                delayTime: null,
                innerPublish: (_, _, _) => Task.CompletedTask,
                cancellationToken: cts.Token
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_rethrow_when_middleware_swallows_outer_cancellation_and_returns_normally()
    {
        // given
        using var cts = new CancellationTokenSource();
        var services = _CreateServices(new MiddlewareCallRecorder());
        services.AddSingleton(cts);
        services.AddScoped<IPublishMiddleware<PublishContext>, SwallowingOuterCancellationPublishMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync(
                new MiddlewarePayload("hi"),
                IntentType.Bus,
                options: null,
                delayTime: null,
                innerPublish: (_, _, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                cancellationToken: AbortToken
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_make_publish_context_read_only_before_middleware_resumes_after_next()
    {
        // given
        var recorder = new MiddlewareCallRecorder();
        var services = _CreateServices(recorder);
        services.AddScoped<
            IPublishMiddleware<PublishContext<MiddlewarePayload>>,
            MutatingAfterNextTypedPublishMiddleware
        >();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            new MiddlewarePayload("hi"),
            IntentType.Bus,
            options: null,
            delayTime: null,
            innerPublish: (_, _, _) => Task.CompletedTask,
            cancellationToken: AbortToken
        );

        // then
        recorder.Calls.Should().Equal("mutation-blocked");
    }

    [Fact]
    public async Task should_make_publish_context_read_only_when_inner_middleware_short_circuits()
    {
        // given
        var recorder = new MiddlewareCallRecorder();
        var services = _CreateServices(recorder);
        services.AddScoped<IPublishMiddleware<PublishContext>, MutatingAfterNextBusPublishMiddleware>();
        services.AddScoped<IPublishMiddleware<PublishContext<MiddlewarePayload>>, ShortCircuitTypedPublishMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            new MiddlewarePayload("hi"),
            IntentType.Bus,
            options: null,
            delayTime: null,
            innerPublish: (_, _, _) => Task.CompletedTask,
            cancellationToken: AbortToken
        );

        // then
        recorder.Calls.Should().Equal("typed.short-circuit", "mutation-blocked");
    }

    [Fact]
    public async Task should_keep_direct_bus_middleware_when_unmatched_typed_descriptor_exists()
    {
        // given
        var recorder = new MiddlewareCallRecorder();
        var services = _CreateServices(recorder);
        services.AddScoped<IPublishMiddleware<PublishContext>, RecordingPublishMiddlewareA>();
        new MessagingBuilder(services).AddPublishMiddlewareFor<
            MutatingAfterNextTypedPublishMiddleware,
            MiddlewarePayload
        >();
        var provider = services.BuildServiceProvider();
        var pipeline = new PublishMiddlewarePipeline(
            provider,
            provider.GetRequiredService<IMiddlewareDescriptorRegistry>()
        );

        // when
        await pipeline.ExecuteAsync(
            new OtherMiddlewarePayload("hi"),
            IntentType.Bus,
            options: null,
            delayTime: null,
            innerPublish: (_, _, _) =>
            {
                recorder.Record("inner");
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        recorder.Calls.Should().Equal("A.before", "inner", "A.after");
    }

    private static ServiceCollection _CreateServices(MiddlewareCallRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        return services;
    }

    private static IPublishMiddlewarePipeline _BuildPipeline(ServiceCollection services)
    {
        return new PublishMiddlewarePipeline(services.BuildServiceProvider());
    }
}

internal sealed record MiddlewarePayload(string Value);

internal sealed record OtherMiddlewarePayload(string Value);

internal sealed class MiddlewareCallRecorder
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _calls = new();

    public IReadOnlyList<string> Calls => _calls.ToArray();

    public void Record(string call) => _calls.Enqueue(call);
}

internal sealed class RecordingPublishMiddlewareA(MiddlewareCallRecorder recorder) : IPublishMiddleware<PublishContext>
{
    public async ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
    {
        recorder.Record("A.before");
        await next();
        recorder.Record("A.after");
    }
}

internal sealed class RecordingPublishMiddlewareB(MiddlewareCallRecorder recorder) : IPublishMiddleware<PublishContext>
{
    public async ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
    {
        recorder.Record("B.before");
        await next();
        recorder.Record("B.after");
    }
}

internal sealed class ShortCircuitPublishMiddleware(MiddlewareCallRecorder recorder)
    : IPublishMiddleware<PublishContext>
{
    public ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
    {
        recorder.Record("short-circuit");
        return ValueTask.CompletedTask;
    }
}

internal sealed class PostSuccessThrowingPublishMiddleware(MiddlewareCallRecorder recorder)
    : IPublishMiddleware<PublishContext>
{
    public async ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
    {
        await next();
        recorder.Record("post-success.throw");
        throw new ObjectDisposedException(nameof(PostSuccessThrowingPublishMiddleware));
    }
}

internal sealed class MatchingOceAfterNextPublishMiddleware(CancellationTokenSource source)
    : IPublishMiddleware<PublishContext>
{
    public async ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
    {
        await next();
        await source.CancelAsync();
        throw new OperationCanceledException(source.Token);
    }
}

internal sealed class MixedAggregateAfterNextPublishMiddleware(CancellationTokenSource source)
    : IPublishMiddleware<PublishContext>
{
    public async ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
    {
        await next();
        await source.CancelAsync();
        throw new AggregateException(
            new InvalidOperationException("diagnostic sibling"),
            new OperationCanceledException(source.Token)
        );
    }
}

internal sealed class AggregateBeforeNextPublishMiddleware(CancellationTokenSource source)
    : IPublishMiddleware<PublishContext>
{
    public async ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
    {
        await source.CancelAsync();
        throw new AggregateException(
            new InvalidOperationException("diagnostic sibling"),
            new OperationCanceledException(source.Token)
        );
    }
}

internal sealed class SwallowingOuterCancellationPublishMiddleware(CancellationTokenSource source)
    : IPublishMiddleware<PublishContext>
{
    public async ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
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

internal sealed class MutatingAfterNextTypedPublishMiddleware(MiddlewareCallRecorder recorder)
    : IPublishMiddleware<PublishContext<MiddlewarePayload>>
{
    public async ValueTask InvokeAsync(PublishContext<MiddlewarePayload> context, Func<ValueTask> next)
    {
        await next();

        try
        {
            context.Options = new PublishOptions { TenantId = "too-late" };
        }
        catch (InvalidOperationException)
        {
            recorder.Record("mutation-blocked");
        }
    }
}

internal sealed class MutatingAfterNextBusPublishMiddleware(MiddlewareCallRecorder recorder)
    : IPublishMiddleware<PublishContext>
{
    public async ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
    {
        await next();

        try
        {
            context.WithOptions(new PublishOptions { TenantId = "too-late" });
        }
        catch (InvalidOperationException)
        {
            recorder.Record("mutation-blocked");
        }
    }
}

internal sealed class ShortCircuitTypedPublishMiddleware(MiddlewareCallRecorder recorder)
    : IPublishMiddleware<PublishContext<MiddlewarePayload>>
{
    public ValueTask InvokeAsync(PublishContext<MiddlewarePayload> context, Func<ValueTask> next)
    {
        recorder.Record("typed.short-circuit");
        return ValueTask.CompletedTask;
    }
}
