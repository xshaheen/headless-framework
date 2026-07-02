// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Testing.Tests;

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
namespace Tests.Configuration;

public sealed class MessagingOptionsValidatorMiddlewareTests : TestBase
{
    [Fact]
    public void should_reject_typed_consume_middleware_registered_at_bus_scope()
    {
        // given
        var registry = new MiddlewareDescriptorRegistry();
        registry.AddOrGet(
            new MiddlewareDescriptorInput(
                MiddlewareDirection.Consume,
                MiddlewareScope.Bus,
                typeof(TypedBusConsumeMiddleware),
                typeof(IConsumeMiddleware<ConsumeContext<OrderPlaced>>),
                typeof(ConsumeContext<OrderPlaced>),
                MessageType: null,
                GroupName: null
            )
        );
        var validator = new MessagingOptionsValidator(registry);

        // when
        var act = () => validator.Validate(new MessagingOptions());

        // then
        act.Should()
            .Throw<MessagingConfigurationException>()
            .WithMessage("*TypedBusConsumeMiddleware*bus scope*typed context*AddConsumeMiddlewareFor*");
    }

    [Fact]
    public void should_reject_typed_publish_middleware_registered_at_bus_scope()
    {
        // given
        var registry = new MiddlewareDescriptorRegistry();
        registry.AddOrGet(
            new MiddlewareDescriptorInput(
                MiddlewareDirection.Publish,
                MiddlewareScope.Bus,
                typeof(TypedBusPublishMiddleware),
                typeof(IPublishMiddleware<PublishingContext<OrderPlaced>>),
                typeof(PublishingContext<OrderPlaced>),
                MessageType: null,
                GroupName: null
            )
        );
        var validator = new MessagingOptionsValidator(registry);

        // when
        var act = () => validator.Validate(new MessagingOptions());

        // then
        act.Should()
            .Throw<MessagingConfigurationException>()
            .WithMessage("*TypedBusPublishMiddleware*bus scope*typed context*AddPublishMiddlewareFor*");
    }

    [Fact]
    public void should_accept_object_typed_bus_middleware()
    {
        // given
        var registry = new MiddlewareDescriptorRegistry();
        registry.AddOrGet(
            new MiddlewareDescriptorInput(
                MiddlewareDirection.Consume,
                MiddlewareScope.Bus,
                typeof(ObjectBusConsumeMiddleware),
                typeof(IConsumeMiddleware<ConsumeContext>),
                typeof(ConsumeContext),
                MessageType: null,
                GroupName: null
            )
        );
        var validator = new MessagingOptionsValidator(registry);

        // when
        var result = validator.Validate(new MessagingOptions());

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_accept_typed_middleware_at_message_scope()
    {
        // given
        var registry = new MiddlewareDescriptorRegistry();
        registry.AddOrGet(
            new MiddlewareDescriptorInput(
                MiddlewareDirection.Consume,
                MiddlewareScope.Message,
                typeof(TypedBusConsumeMiddleware),
                typeof(IConsumeMiddleware<ConsumeContext<OrderPlaced>>),
                typeof(ConsumeContext<OrderPlaced>),
                typeof(OrderPlaced),
                "checkout"
            )
        );
        var validator = new MessagingOptionsValidator(registry);

        // when
        var result = validator.Validate(new MessagingOptions());

        // then
        result.IsValid.Should().BeTrue();
    }

    private sealed record OrderPlaced(string OrderId);

    private sealed class TypedBusConsumeMiddleware : IConsumeMiddleware<ConsumeContext<OrderPlaced>>
    {
        public ValueTask InvokeAsync(ConsumeContext<OrderPlaced> context, Func<ValueTask> next) => next();
    }

    private sealed class TypedBusPublishMiddleware : IPublishMiddleware<PublishingContext<OrderPlaced>>
    {
        public ValueTask InvokeAsync(PublishingContext<OrderPlaced> context, Func<ValueTask> next) => next();
    }

    private sealed class ObjectBusConsumeMiddleware : IConsumeMiddleware<ConsumeContext>
    {
        public ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next) => next();
    }
}
