// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Internal;

public sealed class PublishMiddlewarePipelineMigratedTests : TestBase
{
    [Fact]
    public async Task should_invoke_inner_publish_with_caller_options_when_no_middleware_registered()
    {
        // given
        var pipeline = _BuildPipeline(new ServiceCollection());
        var callerOptions = new PublishOptions { TenantId = "caller", MessageName = "orders" };
        PublishOptions? observedOptions = null;
        TimeSpan? observedDelay = null;

        // when
        await pipeline.ExecuteAsync(
            new MigratedPublishMessage("order-1"),
            IntentType.Bus,
            callerOptions,
            TimeSpan.FromSeconds(5),
            (options, delay, _) =>
            {
                observedOptions = (PublishOptions?)options;
                observedDelay = delay;
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        observedOptions.Should().BeSameAs(callerOptions);
        observedDelay.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task should_thread_middleware_mutated_options_through_to_inner_publish()
    {
        // given
        var services = new ServiceCollection();
        services.AddScoped<
            IPublishMiddleware<PublishContext<MigratedPublishMessage>>,
            TenantStampingPublishMiddleware
        >();
        var pipeline = _BuildPipeline(services);
        PublishOptions? observedOptions = null;

        // when
        await pipeline.ExecuteAsync(
            new MigratedPublishMessage("order-1"),
            IntentType.Bus,
            new PublishOptions { CorrelationId = "corr-1" },
            delayTime: null,
            (options, _, _) =>
            {
                observedOptions = (PublishOptions?)options;
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        observedOptions!.TenantId.Should().Be("tenant-from-middleware");
        observedOptions.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task should_thread_middleware_mutated_delay_through_to_inner_publish()
    {
        // given
        var services = new ServiceCollection();
        services.AddScoped<
            IPublishMiddleware<PublishContext<MigratedPublishMessage>>,
            DelayMultiplyingPublishMiddleware
        >();
        var pipeline = _BuildPipeline(services);
        TimeSpan? observedDelay = null;

        // when
        await pipeline.ExecuteAsync(
            new MigratedPublishMessage("order-1"),
            IntentType.Bus,
            options: null,
            TimeSpan.FromSeconds(10),
            (_, delay, _) =>
            {
                observedDelay = delay;
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        observedDelay.Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task should_discard_caller_options_when_middleware_assigns_null_options()
    {
        // given
        var services = new ServiceCollection();
        services.AddScoped<
            IPublishMiddleware<PublishContext<MigratedPublishMessage>>,
            NullingOptionsPublishMiddleware
        >();
        var pipeline = _BuildPipeline(services);
        PublishOptions? observedOptions = new() { TenantId = "unset" };

        // when
        await pipeline.ExecuteAsync(
            new MigratedPublishMessage("order-1"),
            IntentType.Bus,
            new PublishOptions { TenantId = "caller" },
            delayTime: null,
            (options, _, _) =>
            {
                observedOptions = (PublishOptions?)options;
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        observedOptions.Should().BeNull();
    }

    [Fact]
    public async Task should_drop_to_immediate_publish_when_middleware_nulls_delay_time()
    {
        // given
        var services = new ServiceCollection();
        services.AddScoped<IPublishMiddleware<PublishContext<MigratedPublishMessage>>, DelayNullingPublishMiddleware>();
        var pipeline = _BuildPipeline(services);
        TimeSpan? observedDelay = TimeSpan.FromSeconds(1);

        // when
        await pipeline.ExecuteAsync(
            new MigratedPublishMessage("order-1"),
            IntentType.Bus,
            options: null,
            TimeSpan.FromMinutes(5),
            (_, delay, _) =>
            {
                observedDelay = delay;
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        observedDelay.Should().BeNull();
    }

    [Fact]
    public async Task should_propagate_inner_publish_exception_when_no_middleware_handles_it()
    {
        // given
        var pipeline = _BuildPipeline(new ServiceCollection());

        // when
        var act = async () =>
            await pipeline.ExecuteAsync<MigratedPublishMessage>(
                content: null,
                IntentType.Bus,
                options: null,
                delayTime: null,
                innerPublish: (_, _, _) => throw new InvalidOperationException("inner failed"),
                cancellationToken: AbortToken
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("inner failed");
    }

    [Fact]
    public async Task should_resolve_separate_middleware_instances_per_publish_for_singleton_pipeline()
    {
        // given
        var recorder = new MigratedPublishRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped<
            IPublishMiddleware<PublishContext<MigratedPublishMessage>>,
            InstanceTrackingPublishMiddleware
        >();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            new MigratedPublishMessage("first"),
            IntentType.Bus,
            options: null,
            delayTime: null,
            (_, _, _) => Task.CompletedTask,
            cancellationToken: AbortToken
        );
        await pipeline.ExecuteAsync(
            new MigratedPublishMessage("second"),
            IntentType.Bus,
            options: null,
            delayTime: null,
            (_, _, _) => Task.CompletedTask,
            cancellationToken: AbortToken
        );

        // then
        recorder.InstanceIds.Should().HaveCount(2);
        recorder.InstanceIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task should_invoke_bus_middleware_before_typed_publish_middleware()
    {
        // given
        var recorder = new MigratedPublishRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped<IPublishMiddleware<PublishContext>, RecordingBusPublishMiddleware>();
        services.AddScoped<
            IPublishMiddleware<PublishContext<MigratedPublishMessage>>,
            RecordingTypedPublishMiddleware
        >();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            new MigratedPublishMessage("order-1"),
            IntentType.Bus,
            options: null,
            delayTime: null,
            (_, _, _) =>
            {
                recorder.Record("inner");
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        recorder.Calls.Should().Equal("bus.before", "typed.before", "inner", "typed.after", "bus.after");
    }

    [Fact]
    public async Task should_skip_typed_publish_middleware_for_other_message_type()
    {
        // given
        var recorder = new MigratedPublishRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped<
            IPublishMiddleware<PublishContext<MigratedPublishMessage>>,
            RecordingTypedPublishMiddleware
        >();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            new OtherMigratedPublishMessage("other"),
            IntentType.Bus,
            options: null,
            delayTime: null,
            (_, _, _) =>
            {
                recorder.Record("inner");
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        recorder.Calls.Should().Equal("inner");
    }

    private static IPublishMiddlewarePipeline _BuildPipeline(ServiceCollection services)
    {
        return new PublishMiddlewarePipeline(services.BuildServiceProvider());
    }

    private sealed record MigratedPublishMessage(string Id);

    private sealed record OtherMigratedPublishMessage(string Id);

    private sealed class MigratedPublishRecorder
    {
        private readonly ConcurrentQueue<string> _calls = [];
        private readonly ConcurrentQueue<Guid> _instanceIds = [];

        public IReadOnlyList<string> Calls => _calls.ToArray();
        public IReadOnlyList<Guid> InstanceIds => _instanceIds.ToArray();

        public void Record(string call)
        {
            _calls.Enqueue(call);
        }

        public void RecordInstance(Guid id)
        {
            _instanceIds.Enqueue(id);
        }
    }

    private sealed class TenantStampingPublishMiddleware : IPublishMiddleware<PublishContext<MigratedPublishMessage>>
    {
        public ValueTask InvokeAsync(PublishContext<MigratedPublishMessage> context, Func<ValueTask> next)
        {
            context.Options = (context.Options ?? new PublishOptions()) with { TenantId = "tenant-from-middleware" };
            return next();
        }
    }

    private sealed class DelayMultiplyingPublishMiddleware : IPublishMiddleware<PublishContext<MigratedPublishMessage>>
    {
        public ValueTask InvokeAsync(PublishContext<MigratedPublishMessage> context, Func<ValueTask> next)
        {
            context.DelayTime *= 2;
            return next();
        }
    }

    private sealed class NullingOptionsPublishMiddleware : IPublishMiddleware<PublishContext<MigratedPublishMessage>>
    {
        public ValueTask InvokeAsync(PublishContext<MigratedPublishMessage> context, Func<ValueTask> next)
        {
            context.Options = null;
            return next();
        }
    }

    private sealed class DelayNullingPublishMiddleware : IPublishMiddleware<PublishContext<MigratedPublishMessage>>
    {
        public ValueTask InvokeAsync(PublishContext<MigratedPublishMessage> context, Func<ValueTask> next)
        {
            context.DelayTime = null;
            return next();
        }
    }

    private sealed class InstanceTrackingPublishMiddleware(MigratedPublishRecorder recorder)
        : IPublishMiddleware<PublishContext<MigratedPublishMessage>>
    {
        private readonly Guid _id = Guid.NewGuid();

        public ValueTask InvokeAsync(PublishContext<MigratedPublishMessage> context, Func<ValueTask> next)
        {
            recorder.RecordInstance(_id);
            return next();
        }
    }

    private sealed class RecordingBusPublishMiddleware(MigratedPublishRecorder recorder)
        : IPublishMiddleware<PublishContext>
    {
        public async ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
        {
            recorder.Record("bus.before");
            await next().ConfigureAwait(false);
            recorder.Record("bus.after");
        }
    }

    private sealed class RecordingTypedPublishMiddleware(MigratedPublishRecorder recorder)
        : IPublishMiddleware<PublishContext<MigratedPublishMessage>>
    {
        public async ValueTask InvokeAsync(PublishContext<MigratedPublishMessage> context, Func<ValueTask> next)
        {
            recorder.Record("typed.before");
            await next().ConfigureAwait(false);
            recorder.Record("typed.after");
        }
    }
}
