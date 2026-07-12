// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.MultiTenancy;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;

namespace Tests.MultiTenancy;

public sealed class TenantPropagationPublishMiddlewareTests : TestBase
{
    [Fact]
    public async Task should_stamp_tenant_id_from_ambient_before_next()
    {
        // given
        var currentTenant = new TestCurrentTenant { Id = "acme" };
        var middleware = new TenantPropagationPublishMiddleware(currentTenant);
        var context = new PublishContext<Payload>(new Payload("hello"), IntentType.Bus, options: null, delayTime: null);
        string? observedDuringNext = null;

        // when
        await middleware.InvokeAsync(
            context,
            () =>
            {
                observedDuringNext = context.Options?.TenantId;
                return ValueTask.CompletedTask;
            }
        );

        // then
        observedDuringNext.Should().Be("acme");
        context.Options!.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task should_preserve_caller_set_tenant_id_when_ambient_is_also_set()
    {
        // given
        var currentTenant = new TestCurrentTenant { Id = "acme" };
        var middleware = new TenantPropagationPublishMiddleware(currentTenant);
        var context = new PublishContext<Payload>(
            new Payload("hello"),
            IntentType.Bus,
            new PublishOptions { TenantId = "system" },
            delayTime: null
        );

        // when
        await middleware.InvokeAsync(context, () => ValueTask.CompletedTask);

        // then
        context.Options!.TenantId.Should().Be("system");
    }

    [Fact]
    public async Task should_preserve_other_options_fields_when_stamping_tenant_id()
    {
        // given
        var currentTenant = new TestCurrentTenant { Id = "acme" };
        var middleware = new TenantPropagationPublishMiddleware(currentTenant);
        var context = new PublishContext<Payload>(
            new Payload("hello"),
            IntentType.Bus,
            new PublishOptions { CorrelationId = "corr-1", MessageId = "msg-1" },
            delayTime: null
        );

        // when
        await middleware.InvokeAsync(context, () => ValueTask.CompletedTask);

        // then
        context.Options!.TenantId.Should().Be("acme");
        context.Options.CorrelationId.Should().Be("corr-1");
        context.Options.MessageId.Should().Be("msg-1");
    }

    [Fact]
    public async Task should_skip_stamping_when_ambient_tenant_is_null_or_whitespace()
    {
        // given
        var currentTenant = new TestCurrentTenant();
        var middleware = new TenantPropagationPublishMiddleware(currentTenant);
        var nullContext = new PublishContext<Payload>(
            new Payload("hello"),
            IntentType.Bus,
            options: null,
            delayTime: null
        );
        var whitespaceContext = new PublishContext<Payload>(
            new Payload("hello"),
            IntentType.Bus,
            options: null,
            delayTime: null
        );

        // when
        await middleware.InvokeAsync(nullContext, () => ValueTask.CompletedTask);
        currentTenant.Id = "   ";
        await middleware.InvokeAsync(whitespaceContext, () => ValueTask.CompletedTask);

        // then
        nullContext.Options.Should().BeNull();
        whitespaceContext.Options.Should().BeNull();
    }

    [Fact]
    public async Task should_skip_stamping_when_ambient_tenant_exceeds_max_length()
    {
        // given
        var currentTenant = new TestCurrentTenant { Id = new string('x', MessageOptions.TenantIdMaxLength + 1) };
        var middleware = new TenantPropagationPublishMiddleware(currentTenant);
        var context = new PublishContext<Payload>(new Payload("hello"), IntentType.Bus, options: null, delayTime: null);

        // when
        await middleware.InvokeAsync(context, () => ValueTask.CompletedTask);

        // then
        context.Options.Should().BeNull();
    }

    [Fact]
    public async Task should_stamp_when_ambient_tenant_is_exactly_max_length()
    {
        // given
        var exactlyMax = new string('x', MessageOptions.TenantIdMaxLength);
        var currentTenant = new TestCurrentTenant { Id = exactlyMax };
        var middleware = new TenantPropagationPublishMiddleware(currentTenant);
        var context = new PublishContext<Payload>(new Payload("hello"), IntentType.Bus, options: null, delayTime: null);

        // when
        await middleware.InvokeAsync(context, () => ValueTask.CompletedTask);

        // then
        context.Options!.TenantId.Should().Be(exactlyMax);
    }

    [Fact]
    public void should_throw_argument_null_exception_when_constructed_with_null_tenant()
    {
        // when
        var act = () => new TenantPropagationPublishMiddleware(currentTenant: null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    private sealed record Payload(string Value);
}
