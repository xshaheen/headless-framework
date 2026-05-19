// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.MultiTenancy;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;

namespace Tests.MultiTenancy;

public sealed class TenantPropagationConsumeMiddlewareTests : TestBase
{
    [Fact]
    public async Task should_make_tenant_context_visible_during_next()
    {
        // given
        var currentTenant = new TestCurrentTenant { Id = "ambient" };
        var middleware = new TenantPropagationConsumeMiddleware(currentTenant);
        var context = _CreateContext("acme");
        string? observedDuringNext = null;

        // when
        await middleware.InvokeAsync(
            context,
            () =>
            {
                observedDuringNext = currentTenant.Id;
                return ValueTask.CompletedTask;
            }
        );

        // then
        observedDuringNext.Should().Be("acme");
        currentTenant.Id.Should().Be("ambient");
    }

    [Fact]
    public async Task should_leave_ambient_tenant_unchanged_when_context_has_no_tenant()
    {
        // given
        var currentTenant = new TestCurrentTenant { Id = "ambient" };
        var middleware = new TenantPropagationConsumeMiddleware(currentTenant);
        var context = _CreateContext(tenantId: null);
        string? observedDuringNext = null;

        // when
        await middleware.InvokeAsync(
            context,
            () =>
            {
                observedDuringNext = currentTenant.Id;
                return ValueTask.CompletedTask;
            }
        );

        // then
        observedDuringNext.Should().Be("ambient");
        currentTenant.Id.Should().Be("ambient");
    }

    [Fact]
    public async Task should_restore_tenant_context_when_next_throws()
    {
        // given
        var currentTenant = new TestCurrentTenant { Id = "ambient" };
        var middleware = new TenantPropagationConsumeMiddleware(currentTenant);
        var context = _CreateContext("acme");

        // when
        var act = async () =>
            await middleware.InvokeAsync(
                context,
                () => ValueTask.FromException(new InvalidOperationException("handler failed"))
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler failed");
        currentTenant.Id.Should().Be("ambient");
    }

    [Fact]
    public async Task should_restore_tenant_context_when_next_is_cancelled()
    {
        // given
        var currentTenant = new TestCurrentTenant { Id = "ambient" };
        var middleware = new TenantPropagationConsumeMiddleware(currentTenant);
        var context = _CreateContext("acme");

        // when
        var act = async () =>
            await middleware.InvokeAsync(context, () => ValueTask.FromCanceled(new CancellationToken(canceled: true)));

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        currentTenant.Id.Should().Be("ambient");
    }

    [Fact]
    public void should_throw_argument_null_exception_when_constructed_with_null_tenant()
    {
        // when
        var act = () => new TenantPropagationConsumeMiddleware(currentTenant: null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    private static ConsumeContext<Payload> _CreateContext(string? tenantId)
    {
        return new ConsumeContext<Payload>
        {
            Message = new Payload("hello"),
            MessageId = "msg-1",
            CorrelationId = null,
            TenantId = tenantId,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "test.topic",
        };
    }

    private sealed record Payload(string Value);
}
