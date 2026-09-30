// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.MultiTenancy;
using Headless.MultiTenancy;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    [Fact]
    public async Task should_enrich_logs_and_span_during_next_and_remove_log_scope_after()
    {
        // given
        var logger = new ScopeRecordingLogger<TenantPropagationConsumeMiddleware>();
        var middleware = new TenantPropagationConsumeMiddleware(
            new TestCurrentTenant(),
            Options.Create(new TenantTelemetryOptions()),
            logger
        );
        using var consumeSpan = RecordedTestActivity.Start();
        IReadOnlyList<KeyValuePair<string, object?>> scopeDuringNext = [];
        object? spanTenantDuringNext = null;

        // when
        await middleware.InvokeAsync(
            _CreateContext("acme"),
            () =>
            {
                scopeDuringNext = logger.GetActiveScopeProperties();
                spanTenantDuringNext = Activity.Current?.GetTagItem("tenant.id");
                return ValueTask.CompletedTask;
            }
        );

        // then
        scopeDuringNext
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<string, object?>("TenantId", "acme"));
        spanTenantDuringNext.Should().Be("acme");
        logger.GetActiveScopeProperties().Should().BeEmpty();
        consumeSpan.Activity.GetTagItem("tenant.id").Should().Be("acme");
    }

    [Fact]
    public async Task should_tag_span_but_not_repeat_log_scope_when_tenant_is_already_ambient()
    {
        // given
        var logger = new ScopeRecordingLogger<TenantPropagationConsumeMiddleware>();
        var middleware = new TenantPropagationConsumeMiddleware(
            new TestCurrentTenant { Id = "acme" },
            Options.Create(new TenantTelemetryOptions()),
            logger
        );
        using var consumeSpan = RecordedTestActivity.Start();
        IReadOnlyList<KeyValuePair<string, object?>> scopeDuringNext = [];

        // when
        await middleware.InvokeAsync(
            _CreateContext("acme"),
            () =>
            {
                scopeDuringNext = logger.GetActiveScopeProperties();
                return ValueTask.CompletedTask;
            }
        );

        // then
        scopeDuringNext.Should().BeEmpty();
        consumeSpan.Activity.GetTagItem("tenant.id").Should().Be("acme");
    }

    [Fact]
    public void should_enrich_exhausted_callback_scope_from_envelope_and_remove_log_scope_on_dispose()
    {
        // given
        var logger = new ScopeRecordingLogger<TenantPropagationConsumeMiddlewareTests>();
        var currentTenant = new TestCurrentTenant();
        using var services = new ServiceCollection()
            .AddOptions()
            .AddSingleton<ICurrentTenant>(currentTenant)
            .BuildServiceProvider();
        var message = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.TenantId] = "acme" },
            value: null
        );
        using var callbackSpan = RecordedTestActivity.Start();

        // when
        var scope = TenantContextScope.ChangeFromEnvelope(services, message, logger);
        var tenantDuringCallback = currentTenant.Id;
        var scopeDuringCallback = logger.GetActiveScopeProperties();
        scope!.Dispose();

        // then
        tenantDuringCallback.Should().Be("acme");
        scopeDuringCallback
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<string, object?>("TenantId", "acme"));
        callbackSpan.Activity.GetTagItem("tenant.id").Should().Be("acme");
        logger.GetActiveScopeProperties().Should().BeEmpty();
        currentTenant.Id.Should().BeNull();
    }

    private static ConsumeContext<Payload> _CreateContext(string? tenantId)
    {
        return new ConsumeContext<Payload>
        {
            Lane = MessageLane.Bus,
            Message = new Payload("hello"),
            MessageId = "msg-1",
            CorrelationId = null,
            TenantId = tenantId,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            MessageName = "test.messageName",
        };
    }

    private sealed record Payload(string Value);
}
