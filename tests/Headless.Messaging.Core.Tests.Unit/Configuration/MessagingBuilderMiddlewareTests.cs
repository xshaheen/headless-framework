// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Configuration;

public sealed class MessagingBuilderMiddlewareTests : TestBase
{
    [Fact]
    public void should_register_bus_consume_middleware_with_scoped_lifetime()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);

        // when
        builder.AddBusConsumeMiddleware<NoopBusConsumeMiddleware>();

        // then
        var descriptor = services.Single(x => x.ImplementationType == typeof(NoopBusConsumeMiddleware));
        descriptor.ServiceType.Should().Be<IConsumeMiddleware<ConsumeContext>>();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void should_not_duplicate_same_bus_consume_middleware_descriptor()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);

        // when
        builder.AddBusConsumeMiddleware<NoopBusConsumeMiddleware>();
        builder.AddBusConsumeMiddleware<NoopBusConsumeMiddleware>();

        // then
        var registry = _GetRegistry(services);
        registry.Descriptors.Should().ContainSingle(x => x.MiddlewareType == typeof(NoopBusConsumeMiddleware));
        services.Count(x => x.ImplementationType == typeof(NoopBusConsumeMiddleware)).Should().Be(1);
    }

    [Fact]
    public void should_record_priority_from_fluent_handle()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);

        // when
        builder.AddBusPublishMiddleware<NoopBusPublishMiddleware>().WithPriority(-500);

        // then
        var descriptor = _GetRegistry(services)
            .Descriptors.Single(x => x.MiddlewareType == typeof(NoopBusPublishMiddleware));
        descriptor.Priority.Should().Be(-500);
    }

    [Fact]
    public void should_record_typed_consume_middleware_group_and_message_type()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);

        // when
        builder.AddConsumeMiddlewareFor<TypedConsumeMiddleware, OrderPlaced>("checkout");

        // then
        var descriptor = _GetRegistry(services)
            .Descriptors.Single(x => x.MiddlewareType == typeof(TypedConsumeMiddleware));
        descriptor.Scope.Should().Be(MiddlewareScope.Message);
        descriptor.Direction.Should().Be(MiddlewareDirection.Consume);
        descriptor.MessageType.Should().Be<OrderPlaced>();
        descriptor.GroupName.Should().Be("checkout");
    }

    [Fact]
    public void should_record_typed_publish_middleware_message_type()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);

        // when
        builder.AddPublishMiddlewareFor<TypedPublishMiddleware, OrderPlaced>();

        // then
        var descriptor = _GetRegistry(services)
            .Descriptors.Single(x => x.MiddlewareType == typeof(TypedPublishMiddleware));
        descriptor.Scope.Should().Be(MiddlewareScope.Message);
        descriptor.Direction.Should().Be(MiddlewareDirection.Publish);
        descriptor.MessageType.Should().Be<OrderPlaced>();
        descriptor.GroupName.Should().BeNull();
    }

    [Fact]
    public void should_default_middleware_priority_to_zero()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);

        // when
        builder.AddBusPublishMiddleware<NoopBusPublishMiddleware>();

        // then
        _GetRegistry(services)
            .Descriptors.Single(x => x.MiddlewareType == typeof(NoopBusPublishMiddleware))
            .Priority.Should()
            .Be(0);
    }

    [Fact]
    public async Task should_dispatch_bus_middleware_by_priority_then_registration_order()
    {
        // given
        var recorder = new MiddlewareOrderRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        var builder = new MessagingBuilder(services);
        builder.AddBusPublishMiddleware<PriorityZeroPublishMiddlewareA>();
        builder.AddBusPublishMiddleware<PriorityMinusPublishMiddleware>().WithPriority(-100);
        builder.AddBusPublishMiddleware<PriorityZeroPublishMiddlewareB>();
        var provider = services.BuildServiceProvider();
        var pipeline = new PublishMiddlewarePipeline(
            provider,
            provider.GetRequiredService<IMiddlewareDescriptorRegistry>()
        );

        // when
        await pipeline.ExecuteAsync(
            new OrderPlaced("order-1"),
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
        recorder
            .Calls.Should()
            .Equal("minus.before", "A.before", "B.before", "inner", "B.after", "A.after", "minus.after");
    }

    private static IMiddlewareDescriptorRegistry _GetRegistry(IServiceCollection services)
    {
        return (IMiddlewareDescriptorRegistry)
            services.Single(x => x.ServiceType == typeof(IMiddlewareDescriptorRegistry)).ImplementationInstance!;
    }

    private sealed record OrderPlaced(string OrderId);

    private sealed class NoopBusConsumeMiddleware : IConsumeMiddleware<ConsumeContext>
    {
        public ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next) => next();
    }

    private sealed class NoopBusPublishMiddleware : IPublishMiddleware<PublishContext>
    {
        public ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next) => next();
    }

    private sealed class TypedConsumeMiddleware : IConsumeMiddleware<ConsumeContext<OrderPlaced>>
    {
        public ValueTask InvokeAsync(ConsumeContext<OrderPlaced> context, Func<ValueTask> next) => next();
    }

    private sealed class TypedPublishMiddleware : IPublishMiddleware<PublishingContext<OrderPlaced>>
    {
        public ValueTask InvokeAsync(PublishingContext<OrderPlaced> context, Func<ValueTask> next) => next();
    }

    private sealed class MiddlewareOrderRecorder
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _calls = new();

        public IReadOnlyList<string> Calls => _calls.ToArray();

        public void Record(string call) => _calls.Enqueue(call);
    }

    private sealed class PriorityZeroPublishMiddlewareA(MiddlewareOrderRecorder recorder)
        : IPublishMiddleware<PublishContext>
    {
        public async ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
        {
            recorder.Record("A.before");
            await next();
            recorder.Record("A.after");
        }
    }

    private sealed class PriorityZeroPublishMiddlewareB(MiddlewareOrderRecorder recorder)
        : IPublishMiddleware<PublishContext>
    {
        public async ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
        {
            recorder.Record("B.before");
            await next();
            recorder.Record("B.after");
        }
    }

    private sealed class PriorityMinusPublishMiddleware(MiddlewareOrderRecorder recorder)
        : IPublishMiddleware<PublishContext>
    {
        public async ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
        {
            recorder.Record("minus.before");
            await next();
            recorder.Record("minus.after");
        }
    }
}
