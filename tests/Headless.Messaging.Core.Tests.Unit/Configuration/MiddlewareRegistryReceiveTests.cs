// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Configuration;

public sealed class MiddlewareRegistryReceiveTests : TestBase
{
    [Fact]
    public void should_match_typed_receive_descriptor_on_exact_type_group_and_lane()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);
        builder.AddReceiveMiddlewareFor<TypedReceiveMiddleware, OrderPlaced>("checkout", MessageLane.Bus);

        var registry = _GetRegistry(services);

        // when
        var exactMatch = registry.TryGetReceiveDescriptors(
            typeof(OrderPlaced),
            "checkout",
            MessageLane.Bus,
            out var matched
        );
        var otherGroup = registry.TryGetReceiveDescriptors(
            typeof(OrderPlaced),
            "fulfillment",
            MessageLane.Bus,
            out var groupMiss
        );
        var otherLane = registry.TryGetReceiveDescriptors(
            typeof(OrderPlaced),
            "checkout",
            MessageLane.Queue,
            out var laneMiss
        );
        var otherType = registry.TryGetReceiveDescriptors(
            typeof(OtherOrderPlaced),
            "checkout",
            MessageLane.Bus,
            out var typeMiss
        );

        // then
        exactMatch.Should().BeTrue();
        matched.Should().ContainSingle().Which.MiddlewareType.Should().Be<TypedReceiveMiddleware>();
        groupMiss.Should().BeEmpty();
        laneMiss.Should().BeEmpty();
        typeMiss.Should().BeEmpty();
        otherGroup.Should().BeFalse();
        otherLane.Should().BeFalse();
        otherType.Should().BeFalse();
    }

    [Fact]
    public void should_return_global_receive_descriptor_for_both_lanes()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);
        builder.AddReceiveMiddleware<GlobalReceiveMiddleware>();

        var registry = _GetRegistry(services);

        // when
        var busLookup = registry.TryGetReceiveDescriptors(
            typeof(OrderPlaced),
            "checkout",
            MessageLane.Bus,
            out var busDescriptors
        );
        var queueLookup = registry.TryGetReceiveDescriptors(
            typeof(OrderPlaced),
            "checkout",
            MessageLane.Queue,
            out var queueDescriptors
        );

        // then
        busLookup.Should().BeTrue();
        queueLookup.Should().BeTrue();
        busDescriptors.Should().ContainSingle(x => x.MiddlewareType == typeof(GlobalReceiveMiddleware));
        queueDescriptors.Should().ContainSingle(x => x.MiddlewareType == typeof(GlobalReceiveMiddleware));
    }

    [Fact]
    public void should_keep_only_global_receive_middleware_for_a_queue_lane_lookup()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);
        builder.AddReceiveMiddleware<GlobalReceiveMiddleware>();
        builder.AddReceiveMiddlewareFor<TypedReceiveMiddleware, OrderPlaced>("checkout", MessageLane.Bus);

        var registry = _GetRegistry(services);

        // when
        var result = registry.TryGetReceiveDescriptors(
            typeof(OrderPlaced),
            "checkout",
            MessageLane.Queue,
            out var descriptors
        );

        // then
        result.Should().BeTrue();
        descriptors.Should().ContainSingle(x => x.MiddlewareType == typeof(GlobalReceiveMiddleware));
    }

    [Fact]
    public void should_order_global_before_typed_then_priority_then_registration_order()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);
        builder.AddReceiveMiddleware<GlobalPriorityZeroReceiveMiddlewareA>();
        builder.AddReceiveMiddleware<GlobalPriorityMinusReceiveMiddleware>().WithPriority(-100);
        builder.AddReceiveMiddleware<GlobalPriorityZeroReceiveMiddlewareB>();
        builder.AddReceiveMiddlewareFor<TypedReceiveMiddleware, OrderPlaced>("checkout", MessageLane.Bus);
        builder
            .AddReceiveMiddlewareFor<TypedPriorityMinusReceiveMiddleware, OrderPlaced>("checkout", MessageLane.Bus)
            .WithPriority(-5);

        var registry = _GetRegistry(services);

        // when
        var result = registry.TryGetReceiveDescriptors(
            typeof(OrderPlaced),
            "checkout",
            MessageLane.Bus,
            out var descriptors
        );

        // then
        result.Should().BeTrue();
        descriptors
            .Select(static descriptor => descriptor.MiddlewareType)
            .Should()
            .Equal(
                typeof(GlobalPriorityMinusReceiveMiddleware),
                typeof(GlobalPriorityZeroReceiveMiddlewareA),
                typeof(GlobalPriorityZeroReceiveMiddlewareB),
                typeof(TypedPriorityMinusReceiveMiddleware),
                typeof(TypedReceiveMiddleware)
            );
    }

    [Fact]
    public void should_apply_configured_group_prefix_to_typed_receive_middleware_group()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddHeadlessMessaging(options => options.Options.GroupNamePrefix = "tenant");

        // when
        builder.AddReceiveMiddlewareFor<TypedReceiveMiddleware, OrderPlaced>("checkout", MessageLane.Bus);

        // then
        var registry = _GetRegistry(services);
        var descriptor = registry.Descriptors.Single(x => x.MiddlewareType == typeof(TypedReceiveMiddleware));
        descriptor.GroupName.Should().Be("tenant.checkout");

        registry
            .TryGetReceiveDescriptors(typeof(OrderPlaced), "checkout", MessageLane.Bus, out var unprefixed)
            .Should()
            .BeFalse();
        unprefixed.Should().BeEmpty();

        registry
            .TryGetReceiveDescriptors(typeof(OrderPlaced), "tenant.checkout", MessageLane.Bus, out var prefixed)
            .Should()
            .BeTrue();
        prefixed.Should().ContainSingle().Which.MiddlewareType.Should().Be<TypedReceiveMiddleware>();
    }

    [Fact]
    public void should_not_duplicate_same_receive_middleware_descriptor()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);

        // when
        builder.AddReceiveMiddleware<GlobalReceiveMiddleware>();
        builder.AddReceiveMiddleware<GlobalReceiveMiddleware>();

        // then
        _GetRegistry(services)
            .Descriptors.Should()
            .ContainSingle(x => x.MiddlewareType == typeof(GlobalReceiveMiddleware));
        services.Count(x => x.ImplementationType == typeof(GlobalReceiveMiddleware)).Should().Be(1);
    }

    [Fact]
    public void should_register_receive_middleware_as_scoped_ireceivemiddleware()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);

        // when
        builder.AddReceiveMiddleware<GlobalReceiveMiddleware>();
        builder.AddReceiveMiddlewareFor<TypedReceiveMiddleware, OrderPlaced>("checkout", MessageLane.Bus);

        // then
        services
            .Where(x =>
                x.ImplementationType == typeof(GlobalReceiveMiddleware)
                || x.ImplementationType == typeof(TypedReceiveMiddleware)
            )
            .Should()
            .AllSatisfy(descriptor =>
            {
                descriptor.ServiceType.Should().Be<IReceiveMiddleware>();
                descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
            });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetServices<IReceiveMiddleware>();
        resolved
            .Select(static middleware => middleware.GetType())
            .Should()
            .Equal(typeof(GlobalReceiveMiddleware), typeof(TypedReceiveMiddleware));
    }

    [Fact]
    public void should_record_receive_descriptor_identity()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);

        // when
        builder.AddReceiveMiddlewareFor<TypedReceiveMiddleware, OrderPlaced>("checkout", MessageLane.Queue);

        // then
        var descriptor = _GetRegistry(services)
            .Descriptors.Single(x => x.MiddlewareType == typeof(TypedReceiveMiddleware));
        descriptor.Direction.Should().Be(MiddlewareDirection.Receive);
        descriptor.Scope.Should().Be(MiddlewareScope.Message);
        descriptor.MessageType.Should().Be<OrderPlaced>();
        descriptor.GroupName.Should().Be("checkout");
        descriptor.Lane.Should().Be(MessageLane.Queue);
        descriptor.Priority.Should().Be(0);
    }

    private static IMiddlewareDescriptorRegistry _GetRegistry(IServiceCollection services)
    {
        return (IMiddlewareDescriptorRegistry)
            services.Single(x => x.ServiceType == typeof(IMiddlewareDescriptorRegistry)).ImplementationInstance!;
    }

    private sealed record OrderPlaced(string OrderId);

    private sealed record OtherOrderPlaced(string OrderId);

    private sealed class GlobalReceiveMiddleware : IReceiveMiddleware
    {
        public ValueTask InvokeAsync(ReceiveContext context, Func<ValueTask> next)
        {
            return next();
        }
    }

    private sealed class TypedReceiveMiddleware : IReceiveMiddleware
    {
        public ValueTask InvokeAsync(ReceiveContext context, Func<ValueTask> next)
        {
            return next();
        }
    }

    private sealed class GlobalPriorityZeroReceiveMiddlewareA : IReceiveMiddleware
    {
        public ValueTask InvokeAsync(ReceiveContext context, Func<ValueTask> next)
        {
            return next();
        }
    }

    private sealed class GlobalPriorityZeroReceiveMiddlewareB : IReceiveMiddleware
    {
        public ValueTask InvokeAsync(ReceiveContext context, Func<ValueTask> next)
        {
            return next();
        }
    }

    private sealed class GlobalPriorityMinusReceiveMiddleware : IReceiveMiddleware
    {
        public ValueTask InvokeAsync(ReceiveContext context, Func<ValueTask> next)
        {
            return next();
        }
    }

    private sealed class TypedPriorityMinusReceiveMiddleware : IReceiveMiddleware
    {
        public ValueTask InvokeAsync(ReceiveContext context, Func<ValueTask> next)
        {
            return next();
        }
    }
}
