// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Configuration;

public sealed class MiddlewareRegistryFreezeTests : TestBase
{
    [Fact]
    public void should_reflect_registrations_until_frozen()
    {
        // given
        var registry = new MiddlewareDescriptorRegistry();
        registry.AddOrGet(_Input(typeof(FirstMiddleware)));
        registry.TryGetConsumeDescriptors(typeof(OrderPlaced), MessageLane.Bus, out var before);

        // when
        registry.AddOrGet(_Input(typeof(SecondMiddleware)));
        registry.TryGetConsumeDescriptors(typeof(OrderPlaced), MessageLane.Bus, out var after);

        // then
        before.Should().ContainSingle();
        after.Should().HaveCount(2);
    }

    [Fact]
    public void should_cache_lookup_per_type_and_lane_once_frozen()
    {
        // given
        var registry = new MiddlewareDescriptorRegistry();
        registry.AddOrGet(_Input(typeof(FirstMiddleware)));
        registry.Freeze();

        // when
        var firstFound = registry.TryGetConsumeDescriptors(typeof(OrderPlaced), MessageLane.Bus, out var first);
        var secondFound = registry.TryGetConsumeDescriptors(typeof(OrderPlaced), MessageLane.Bus, out var second);
        var otherLaneFound = registry.TryGetConsumeDescriptors(typeof(OrderPlaced), MessageLane.Queue, out var other);
        var publishFound = registry.TryGetPublishDescriptors(typeof(OrderPlaced), MessageLane.Bus, out var publish);

        // then
        firstFound.Should().BeTrue();
        secondFound.Should().BeTrue();
        second.Should().BeSameAs(first);
        otherLaneFound.Should().BeFalse("the cache key includes the lane");
        other.Should().BeEmpty();
        publishFound.Should().BeFalse("the cache key includes the direction");
        publish.Should().BeEmpty();
    }

    [Fact]
    public void should_throw_when_registering_new_middleware_after_freeze()
    {
        // given
        var registry = new MiddlewareDescriptorRegistry();
        registry.AddOrGet(_Input(typeof(FirstMiddleware)));
        registry.Freeze();

        // when
        var act = () => registry.AddOrGet(_Input(typeof(SecondMiddleware)));

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*SecondMiddleware*after the messaging host has started*");
    }

    [Fact]
    public void should_return_existing_descriptor_for_repeat_registration_after_freeze()
    {
        // given
        var registry = new MiddlewareDescriptorRegistry();
        var original = registry.AddOrGet(_Input(typeof(FirstMiddleware)));
        registry.Freeze();

        // when
        var repeated = registry.AddOrGet(_Input(typeof(FirstMiddleware)));

        // then
        repeated.Should().BeSameAs(original);
    }

    [Fact]
    public async Task should_freeze_registry_when_host_bootstraps()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
        });
        builder.AddBusConsumeMiddleware<GlobalConsumeMiddleware>();
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IMiddlewareDescriptorRegistry>();

        // when
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var act = () => registry.AddOrGet(_Input(typeof(SecondMiddleware)));

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    private static MiddlewareDescriptorInput _Input(Type middlewareType) =>
        new(
            MiddlewareDirection.Consume,
            MiddlewareScope.Bus,
            middlewareType,
            typeof(IConsumeMiddleware<ConsumeContext>),
            typeof(ConsumeContext),
            MessageType: null,
            Lane: MessageLane.Bus
        );

    private sealed record OrderPlaced(string OrderId);

    private sealed class FirstMiddleware;

    private sealed class SecondMiddleware;

    private sealed class GlobalConsumeMiddleware : IConsumeMiddleware<ConsumeContext>
    {
        public ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next) => next();
    }
}
