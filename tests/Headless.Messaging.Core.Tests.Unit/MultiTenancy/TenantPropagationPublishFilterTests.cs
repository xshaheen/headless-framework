// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.MultiTenancy;
using Headless.Testing.Tests;

namespace Tests.MultiTenancy;

/// <summary>
/// Unit tests for <see cref="TenantPropagationPublishFilter"/>.
/// Covers origin AE3 (publish-side stamping from ambient), AE4 (caller override preserved),
/// AE5 (no ambient → no stamping), AE7 (caller-set value preserved when ambient is null).
/// </summary>
public sealed class TenantPropagationPublishFilterTests : TestBase
{
    [Fact]
    public async Task should_stamp_tenant_id_from_ambient_when_options_unset()
    {
        // given — Covers AE3
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("acme");
        var filter = new TenantPropagationPublishFilter(tenant);
        var ctx = new PublishingContext(
            content: new Payload("hello"),
            messageType: typeof(Payload),
            options: null,
            delayTime: null
        );

        // when
        await filter.OnPublishExecutingAsync(ctx);

        // then
        ctx.Options.Should().NotBeNull();
        ctx.Options!.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task should_preserve_caller_set_tenant_id_when_ambient_is_also_set()
    {
        // given — Covers AE4 (caller override wins)
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("acme");
        var filter = new TenantPropagationPublishFilter(tenant);
        var ctx = new PublishingContext(
            content: new Payload("hello"),
            messageType: typeof(Payload),
            options: new PublishOptions { TenantId = "system" },
            delayTime: null
        );

        // when
        await filter.OnPublishExecutingAsync(ctx);

        // then
        ctx.Options!.TenantId.Should().Be("system");
    }

    [Fact]
    public async Task should_be_a_noop_when_ambient_tenant_is_null()
    {
        // given — Covers AE5 (no ambient + no explicit → no stamping)
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns((string?)null);
        var filter = new TenantPropagationPublishFilter(tenant);
        var ctx = new PublishingContext(
            content: new Payload("hello"),
            messageType: typeof(Payload),
            options: null,
            delayTime: null
        );

        // when
        await filter.OnPublishExecutingAsync(ctx);

        // then — Options stays null; no envelope tenant on the wire
        ctx.Options.Should().BeNull();
    }

    [Fact]
    public async Task should_preserve_caller_set_tenant_id_when_ambient_is_null()
    {
        // given — Covers AE7 (no ambient + explicit caller-set → preserved)
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns((string?)null);
        var filter = new TenantPropagationPublishFilter(tenant);
        var ctx = new PublishingContext(
            content: new Payload("hello"),
            messageType: typeof(Payload),
            options: new PublishOptions { TenantId = "explicit" },
            delayTime: null
        );

        // when
        await filter.OnPublishExecutingAsync(ctx);

        // then
        ctx.Options!.TenantId.Should().Be("explicit");
    }

    [Fact]
    public async Task should_preserve_other_options_fields_when_stamping_tenant_id()
    {
        // given — verify the `with` expression preserves all other fields
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("acme");
        var filter = new TenantPropagationPublishFilter(tenant);
        var ctx = new PublishingContext(
            content: new Payload("hello"),
            messageType: typeof(Payload),
            options: new PublishOptions { CorrelationId = "corr-1", MessageId = "msg-1" },
            delayTime: null
        );

        // when
        await filter.OnPublishExecutingAsync(ctx);

        // then
        ctx.Options!.TenantId.Should().Be("acme");
        ctx.Options.CorrelationId.Should().Be("corr-1");
        ctx.Options.MessageId.Should().Be("msg-1");
    }

    [Fact]
    public void should_throw_argument_null_exception_when_constructed_with_null_tenant()
    {
        // when
        var act = () => new TenantPropagationPublishFilter(currentTenant: null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task should_skip_stamping_when_ambient_tenant_is_whitespace()
    {
        // given — lenient ambient handling: whitespace ICurrentTenant.Id maps to "no stamping"
        // and matches the consume-side _ResolveTenantId contract
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("   ");
        var filter = new TenantPropagationPublishFilter(tenant);
        var ctx = new PublishingContext(
            content: new Payload("hello"),
            messageType: typeof(Payload),
            options: null,
            delayTime: null
        );

        // when
        await filter.OnPublishExecutingAsync(ctx);

        // then — Options stays null; whitespace ambient does not propagate
        ctx.Options.Should().BeNull();
    }

    [Fact]
    public async Task should_skip_stamping_when_ambient_tenant_exceeds_max_length()
    {
        // given — lenient ambient handling: oversized ICurrentTenant.Id maps to "no stamping"
        var tenant = Substitute.For<ICurrentTenant>();
        var oversized = new string('x', PublishOptions.TenantIdMaxLength + 1);
        tenant.Id.Returns(oversized);
        var filter = new TenantPropagationPublishFilter(tenant);
        var ctx = new PublishingContext(
            content: new Payload("hello"),
            messageType: typeof(Payload),
            options: null,
            delayTime: null
        );

        // when
        await filter.OnPublishExecutingAsync(ctx);

        // then — Options stays null; oversized ambient is silently dropped
        ctx.Options.Should().BeNull();
    }

    [Fact]
    public async Task should_stamp_when_ambient_tenant_is_exactly_max_length()
    {
        // given — boundary case: ambient ID exactly TenantIdMaxLength characters must stamp
        var tenant = Substitute.For<ICurrentTenant>();
        var exactlyMax = new string('x', PublishOptions.TenantIdMaxLength);
        tenant.Id.Returns(exactlyMax);
        var filter = new TenantPropagationPublishFilter(tenant);
        var ctx = new PublishingContext(
            content: new Payload("hello"),
            messageType: typeof(Payload),
            options: null,
            delayTime: null
        );

        // when
        await filter.OnPublishExecutingAsync(ctx);

        // then — exact-boundary value is stamped (no off-by-one rejection)
        ctx.Options.Should().NotBeNull();
        ctx.Options!.TenantId.Should().Be(exactlyMax);
    }

    private sealed record Payload(string Value);
}
