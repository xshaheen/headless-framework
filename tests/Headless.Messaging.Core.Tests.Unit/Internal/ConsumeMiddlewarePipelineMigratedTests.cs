// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Internal;

public sealed class ConsumeMiddlewarePipelineMigratedTests : TestBase
{
    [Fact]
    public async Task should_invoke_dispatcher_when_no_middleware_registered()
    {
        // given
        var recorder = new MigratedConsumeRecorder();
        var services = _CreateServices(recorder);
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(),
            new MigratedConsumeMessage("order-1"),
            typeof(MigratedConsumeMessage),
            AbortToken
        );

        // then
        recorder.Calls.Should().Equal("dispatcher");
    }

    [Fact]
    public async Task should_expose_resolved_tenant_id_to_middleware()
    {
        // given
        var recorder = new MigratedConsumeRecorder();
        var services = _CreateServices(recorder);
        new MessagingBuilder(services).AddBusConsumeMiddleware<TenantObservingConsumeMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(tenantHeader: "acme"),
            new MigratedConsumeMessage("order-1"),
            typeof(MigratedConsumeMessage),
            AbortToken
        );

        // then
        recorder.Calls.Should().Contain("tenant:acme");
    }

    [Fact]
    public async Task should_allow_middleware_to_handle_dispatcher_exception()
    {
        // given
        var recorder = new MigratedConsumeRecorder();
        var services = _CreateServices(recorder, shouldThrow: true);
        new MessagingBuilder(services).AddBusConsumeMiddleware<HandlingConsumeMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(),
            new MigratedConsumeMessage("order-1"),
            typeof(MigratedConsumeMessage),
            AbortToken
        );

        // then
        recorder.Calls.Should().Equal("handler.before", "dispatcher.throw", "handler.handled");
    }

    [Fact]
    public async Task should_propagate_dispatcher_exception_when_no_middleware_handles_it()
    {
        // given
        var recorder = new MigratedConsumeRecorder();
        var services = _CreateServices(recorder, shouldThrow: true);
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync(
                _BuildConsumerContext(),
                new MigratedConsumeMessage("order-1"),
                typeof(MigratedConsumeMessage),
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("dispatcher failed");
    }

    [Fact]
    public async Task should_restore_previous_consume_context_when_dispatcher_throws()
    {
        // given
        var recorder = new MigratedConsumeRecorder();
        var services = _CreateServices(recorder, shouldThrow: true);
        var accessor = new AsyncLocalConsumeContextAccessor
        {
            Current = new ConsumeContext<MigratedConsumeMessage>
            {
                Message = new MigratedConsumeMessage("previous"),
                MessageId = "previous-message",
                CorrelationId = "previous-correlation",
                Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
                Timestamp = DateTimeOffset.UtcNow,
                MessageName = "previous",
                IntentType = IntentType.Bus,
            },
        };
        var previous = accessor.Current;
        var pipeline = _BuildPipeline(services, accessor);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync(
                _BuildConsumerContext(),
                new MigratedConsumeMessage("order-1"),
                typeof(MigratedConsumeMessage),
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("dispatcher failed");
        accessor.Current.Should().BeSameAs(previous);
    }

    [Fact]
    public async Task should_invoke_bus_middleware_before_typed_consume_middleware()
    {
        // given
        var recorder = new MigratedConsumeRecorder();
        var services = _CreateServices(recorder);
        var builder = new MessagingBuilder(services);
        builder.AddBusConsumeMiddleware<RecordingBusConsumeMiddleware>();
        builder.AddConsumeMiddlewareFor<RecordingTypedConsumeMiddleware, MigratedConsumeMessage>(
            "checkout",
            MessageLane.Bus
        );
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(groupName: "checkout"),
            new MigratedConsumeMessage("order-1"),
            typeof(MigratedConsumeMessage),
            AbortToken
        );

        // then
        recorder.Calls.Should().Equal("bus.before", "typed.before", "dispatcher", "typed.after", "bus.after");
    }

    [Fact]
    public async Task should_dispatch_bus_consume_middleware_by_priority_then_registration_order()
    {
        // given
        var recorder = new MigratedConsumeRecorder();
        var services = _CreateServices(recorder);
        var builder = new MessagingBuilder(services);
        builder.AddBusConsumeMiddleware<PriorityZeroConsumeMiddlewareA>();
        builder.AddBusConsumeMiddleware<PriorityMinusConsumeMiddleware>().WithPriority(-100);
        builder.AddBusConsumeMiddleware<PriorityZeroConsumeMiddlewareB>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(groupName: "checkout"),
            new MigratedConsumeMessage("order-1"),
            typeof(MigratedConsumeMessage),
            AbortToken
        );

        // then
        recorder
            .Calls.Should()
            .Equal("minus.before", "A.before", "B.before", "dispatcher", "B.after", "A.after", "minus.after");
    }

    [Fact]
    public async Task should_keep_direct_bus_consume_middleware_when_typed_descriptor_group_does_not_match()
    {
        // given
        var recorder = new MigratedConsumeRecorder();
        var services = _CreateServices(recorder);
        services.AddScoped<IConsumeMiddleware<ConsumeContext>, RecordingBusConsumeMiddleware>();
        new MessagingBuilder(services).AddConsumeMiddlewareFor<RecordingTypedConsumeMiddleware, MigratedConsumeMessage>(
            "checkout",
            MessageLane.Bus
        );
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(groupName: "reporting"),
            new MigratedConsumeMessage("order-1"),
            typeof(MigratedConsumeMessage),
            AbortToken
        );

        // then
        recorder.Calls.Should().Equal("bus.before", "dispatcher", "bus.after");
    }

    [Fact]
    public async Task should_skip_typed_consume_middleware_for_other_group()
    {
        // given
        var recorder = new MigratedConsumeRecorder();
        var services = _CreateServices(recorder);
        new MessagingBuilder(services).AddConsumeMiddlewareFor<RecordingTypedConsumeMiddleware, MigratedConsumeMessage>(
            "checkout",
            MessageLane.Bus
        );
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(groupName: "reporting"),
            new MigratedConsumeMessage("order-1"),
            typeof(MigratedConsumeMessage),
            AbortToken
        );

        // then
        recorder.Calls.Should().Equal("dispatcher");
    }

    [Fact]
    public async Task should_skip_typed_consume_middleware_for_other_message_type()
    {
        // given
        var recorder = new MigratedConsumeRecorder();
        var services = _CreateServices(recorder);
        new MessagingBuilder(services).AddConsumeMiddlewareFor<RecordingTypedConsumeMiddleware, MigratedConsumeMessage>(
            "checkout",
            MessageLane.Bus
        );
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(groupName: "checkout"),
            new OtherMigratedConsumeMessage("other"),
            typeof(OtherMigratedConsumeMessage),
            AbortToken
        );

        // then
        recorder.Calls.Should().Equal("dispatcher");
    }

    [Fact]
    public async Task should_resolve_separate_middleware_instances_per_consume_for_singleton_pipeline()
    {
        // given
        var recorder = new MigratedConsumeRecorder();
        var services = _CreateServices(recorder);
        new MessagingBuilder(services).AddBusConsumeMiddleware<InstanceTrackingConsumeMiddleware>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(),
            new MigratedConsumeMessage("first"),
            typeof(MigratedConsumeMessage),
            AbortToken
        );
        await pipeline.ExecuteAsync(
            _BuildConsumerContext(),
            new MigratedConsumeMessage("second"),
            typeof(MigratedConsumeMessage),
            AbortToken
        );

        // then
        recorder.InstanceIds.Should().HaveCount(2);
        recorder.InstanceIds.Should().OnlyHaveUniqueItems();
    }

    private static ServiceCollection _CreateServices(MigratedConsumeRecorder recorder, bool shouldThrow = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddSingleton<IMessageDispatcher>(new MigratedConsumeDispatcher(recorder, shouldThrow));
        return services;
    }

    private static IConsumeMiddlewarePipeline _BuildPipeline(
        ServiceCollection services,
        IConsumeContextAccessor? consumeContextAccessor = null
    )
    {
        var provider = services.BuildServiceProvider();
        var runtimeRegistry = Substitute.For<IRuntimeConsumerRegistry>();
        return new ConsumeMiddlewarePipeline(
            provider,
            runtimeRegistry,
            provider.GetService<IMiddlewareDescriptorRegistry>(),
            consumeContextAccessor: consumeContextAccessor
        );
    }

    private static ConsumerContext _BuildConsumerContext(string groupName = "checkout", string? tenantHeader = null)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "msg-1",
            [Headers.MessageName] = "orders",
        };

        if (tenantHeader is not null)
        {
            headers[Headers.TenantId] = tenantHeader;
        }

        var descriptor = new ConsumerExecutorDescriptor
        {
            IntentType = IntentType.Bus,
            MethodInfo = typeof(ConsumeMiddlewarePipelineMigratedTests).GetMethod(
                nameof(_BuildConsumerContext),
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
                binder: null,
                types: [typeof(string), typeof(string)],
                modifiers: null
            )!,
            ImplTypeInfo = typeof(ConsumeMiddlewarePipelineMigratedTests).GetTypeInfo(),
            MessageName = "orders",
            GroupName = groupName,
        };

        return new ConsumerContext(
            descriptor,
            new MediumMessage
            {
                StorageId = Guid.NewGuid(),
                Origin = new Message(headers, new MigratedConsumeMessage("stored")),
                Content = "{}",
                IntentType = IntentType.Bus,
                Added = DateTimeOffset.UtcNow,
            }
        );
    }

    private sealed record MigratedConsumeMessage(string Id);

    private sealed record OtherMigratedConsumeMessage(string Id);

    private sealed class MigratedConsumeRecorder
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

    private sealed class TenantObservingConsumeMiddleware(MigratedConsumeRecorder recorder)
        : IConsumeMiddleware<ConsumeContext>
    {
        public ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
        {
            recorder.Record($"tenant:{context.TenantId}");
            return next();
        }
    }

    private sealed class HandlingConsumeMiddleware(MigratedConsumeRecorder recorder)
        : IConsumeMiddleware<ConsumeContext>
    {
        public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
        {
            recorder.Record("handler.before");

            try
            {
                await next().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                recorder.Record("handler.handled");
            }
        }
    }

    private sealed class RecordingBusConsumeMiddleware(MigratedConsumeRecorder recorder)
        : IConsumeMiddleware<ConsumeContext>
    {
        public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
        {
            recorder.Record("bus.before");
            await next().ConfigureAwait(false);
            recorder.Record("bus.after");
        }
    }

    private sealed class PriorityZeroConsumeMiddlewareA(MigratedConsumeRecorder recorder)
        : IConsumeMiddleware<ConsumeContext>
    {
        public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
        {
            recorder.Record("A.before");
            await next().ConfigureAwait(false);
            recorder.Record("A.after");
        }
    }

    private sealed class PriorityZeroConsumeMiddlewareB(MigratedConsumeRecorder recorder)
        : IConsumeMiddleware<ConsumeContext>
    {
        public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
        {
            recorder.Record("B.before");
            await next().ConfigureAwait(false);
            recorder.Record("B.after");
        }
    }

    private sealed class PriorityMinusConsumeMiddleware(MigratedConsumeRecorder recorder)
        : IConsumeMiddleware<ConsumeContext>
    {
        public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
        {
            recorder.Record("minus.before");
            await next().ConfigureAwait(false);
            recorder.Record("minus.after");
        }
    }

    private sealed class RecordingTypedConsumeMiddleware(MigratedConsumeRecorder recorder)
        : IConsumeMiddleware<ConsumeContext<MigratedConsumeMessage>>
    {
        public async ValueTask InvokeAsync(ConsumeContext<MigratedConsumeMessage> context, Func<ValueTask> next)
        {
            recorder.Record("typed.before");
            await next().ConfigureAwait(false);
            recorder.Record("typed.after");
        }
    }

    private sealed class InstanceTrackingConsumeMiddleware(MigratedConsumeRecorder recorder)
        : IConsumeMiddleware<ConsumeContext>
    {
        private readonly Guid _id = Guid.NewGuid();

        public ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
        {
            recorder.RecordInstance(_id);
            return next();
        }
    }

    private sealed class MigratedConsumeDispatcher(MigratedConsumeRecorder recorder, bool shouldThrow)
        : IMessageDispatcher
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
            return DispatchInScopeAsync(serviceProvider, descriptor: null!, context, cancellationToken);
        }

        public Task DispatchInScopeAsync<TMessage>(
            IServiceProvider serviceProvider,
            ConsumerExecutorDescriptor descriptor,
            ConsumeContext<TMessage> context,
            CancellationToken cancellationToken
        )
            where TMessage : class
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (shouldThrow)
            {
                recorder.Record("dispatcher.throw");
                throw new InvalidOperationException("dispatcher failed");
            }

            recorder.Record("dispatcher");
            return Task.CompletedTask;
        }
    }
}
