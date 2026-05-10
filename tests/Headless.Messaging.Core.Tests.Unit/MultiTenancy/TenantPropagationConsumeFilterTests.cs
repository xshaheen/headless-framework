// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.Messaging.MultiTenancy;
using Headless.Testing.Tests;

namespace Tests.MultiTenancy;

/// <summary>
/// Unit tests for <see cref="TenantPropagationConsumeFilter"/>.
/// Covers origin AE1 (consume-side restoration), AE2 (no-op for tenant-less messages),
/// retry-path dispose discipline, and prior-tenant restoration.
/// </summary>
public sealed class TenantPropagationConsumeFilterTests : TestBase
{
    [Fact]
    public async Task should_change_current_tenant_when_envelope_carries_a_tenant_id()
    {
        // given — Covers AE1
        var currentTenant = Substitute.For<ICurrentTenant>();
        var disposable = Substitute.For<IDisposable>();
        currentTenant.Change("acme").Returns(disposable);
        var filter = new TenantPropagationConsumeFilter(currentTenant);
        var ctx = _MakeExecutingContext(tenantId: "acme");

        // when
        await filter.OnSubscribeExecutingAsync(ctx);

        // then
        currentTenant.Received(1).Change("acme");
    }

    [Fact]
    public async Task should_use_resolved_typed_tenant_id_instead_of_reparsing_raw_header()
    {
        // given
        var currentTenant = Substitute.For<ICurrentTenant>();
        var disposable = Substitute.For<IDisposable>();
        currentTenant.Change("typed-tenant").Returns(disposable);
        var filter = new TenantPropagationConsumeFilter(currentTenant);
        var ctx = _MakeExecutingContext(tenantId: "typed-tenant", rawTenantId: "raw-header");

        // when
        await filter.OnSubscribeExecutingAsync(ctx);

        // then
        currentTenant.Received(1).Change("typed-tenant");
        currentTenant.DidNotReceive().Change("raw-header");
    }

    [Fact]
    public async Task should_dispose_tenant_scope_after_executed_phase()
    {
        // given — Covers AE1 (restoration after success)
        var currentTenant = Substitute.For<ICurrentTenant>();
        var disposable = Substitute.For<IDisposable>();
        currentTenant.Change("acme").Returns(disposable);
        var filter = new TenantPropagationConsumeFilter(currentTenant);

        // when
        await filter.OnSubscribeExecutingAsync(_MakeExecutingContext(tenantId: "acme"));
        await filter.OnSubscribeExecutedAsync(_MakeExecutedContext());

        // then
        disposable.Received(1).Dispose();
    }

    [Fact]
    public async Task should_dispose_tenant_scope_on_exception_path()
    {
        // given — Covers AE6 retry/exception preserves dispose discipline
        var currentTenant = Substitute.For<ICurrentTenant>();
        var disposable = Substitute.For<IDisposable>();
        currentTenant.Change("acme").Returns(disposable);
        var filter = new TenantPropagationConsumeFilter(currentTenant);

        // when — executing followed by exception (consumer threw)
        await filter.OnSubscribeExecutingAsync(_MakeExecutingContext(tenantId: "acme"));
        await filter.OnSubscribeExceptionAsync(_MakeExceptionContext());

        // then — disposable was released on the exception path too
        disposable.Received(1).Dispose();
    }

    [Fact]
    public async Task should_be_a_noop_when_envelope_has_no_tenant_id()
    {
        // given — Covers AE2 (no-op for system messages)
        var currentTenant = Substitute.For<ICurrentTenant>();
        var filter = new TenantPropagationConsumeFilter(currentTenant);

        // when
        await filter.OnSubscribeExecutingAsync(_MakeExecutingContext(tenantId: null));
        await filter.OnSubscribeExecutedAsync(_MakeExecutedContext());

        // then
        currentTenant.DidNotReceive().Change(Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task should_be_a_noop_when_envelope_tenant_id_is_whitespace()
    {
        // given — lenient resolution: whitespace maps to "no tenant"
        var currentTenant = Substitute.For<ICurrentTenant>();
        var filter = new TenantPropagationConsumeFilter(currentTenant);

        // when
        await filter.OnSubscribeExecutingAsync(_MakeExecutingContext(tenantId: null, rawTenantId: "   "));

        // then
        currentTenant.DidNotReceive().Change(Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task should_be_a_noop_when_envelope_tenant_id_exceeds_max_length()
    {
        // given — lenient resolution: oversized values map to "no tenant"
        var currentTenant = Substitute.For<ICurrentTenant>();
        var filter = new TenantPropagationConsumeFilter(currentTenant);
        var oversized = new string('x', PublishOptions.TenantIdMaxLength + 1);

        // when
        await filter.OnSubscribeExecutingAsync(_MakeExecutingContext(tenantId: null, rawTenantId: oversized));

        // then
        currentTenant.DidNotReceive().Change(Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task should_not_dispose_when_no_change_was_made()
    {
        // given — null tenant → no Change → no scope to dispose
        var currentTenant = Substitute.For<ICurrentTenant>();
        var filter = new TenantPropagationConsumeFilter(currentTenant);

        // when
        await filter.OnSubscribeExecutingAsync(_MakeExecutingContext(tenantId: null));
        await filter.OnSubscribeExecutedAsync(_MakeExecutedContext());

        // then — no Change call → no Disposable was returned → nothing to verify on Dispose,
        //         and no NullReferenceException from disposing a non-existent scope.
        currentTenant.DidNotReceive().Change(Arg.Any<string?>());
    }

    [Fact]
    public void should_throw_argument_null_exception_when_constructed_with_null_tenant()
    {
        // when
        var act = () => new TenantPropagationConsumeFilter(currentTenant: null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    private static ExecutingContext _MakeExecutingContext(string? tenantId, string? rawTenantId = null)
    {
        return new ExecutingContext(
            _MakeConsumerContext(rawTenantId ?? tenantId),
            [ /*consumeContext*/
                null, /*ct*/
                CancellationToken.None,
            ],
            tenantId
        );
    }

    private static ExecutedContext _MakeExecutedContext()
    {
        return new ExecutedContext(_MakeConsumerContext(tenantId: "ignored-on-this-phase"), result: null);
    }

    private static ExceptionContext _MakeExceptionContext()
    {
        return new ExceptionContext(
            _MakeConsumerContext(tenantId: "ignored-on-this-phase"),
            new InvalidOperationException("consumer body threw")
        );
    }

    private static ConsumerContext _MakeConsumerContext(string? tenantId)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "msg-1",
            [Headers.MessageName] = "test.topic",
        };
        if (tenantId is not null)
        {
            headers[Headers.TenantId] = tenantId;
        }

        var origin = new Message(headers, value: null);
        var medium = new MediumMessage
        {
            StorageId = 1L,
            Origin = origin,
            Content = "{}",
            Added = DateTime.UtcNow,
        };
        var descriptor = new ConsumerExecutorDescriptor
        {
            MethodInfo = typeof(TenantPropagationConsumeFilterTests).GetMethod(
                nameof(_MakeConsumerContext),
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
                binder: null,
                types: [typeof(string)],
                modifiers: null
            )!,
            ImplTypeInfo = typeof(TenantPropagationConsumeFilterTests).GetTypeInfo(),
            TopicName = "test.topic",
            GroupName = "test-group",
        };

        return new ConsumerContext(descriptor, medium);
    }
}
