// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests.Internal;

/// <summary>
/// Verifies the U10 (#238) strict-tenancy publish guard added on top of the U2 (#228)
/// 4-case header integrity policy. See
/// docs/plans/2026-05-03-002-feat-messaging-phase1-foundations-plan.md U10.
/// </summary>
public sealed class StrictTenancyPublishGuardTests : TestBase
{
    private sealed record TestMessage(string Value);

    [Fact]
    public void should_publish_with_null_tenant_when_strict_tenancy_disabled_by_default()
    {
        // given
        var factory = _CreateFactory(tenantContextRequired: false);

        // when
        var prepared = factory.Create(new TestMessage("v"), options: null);

        // then
        prepared.Message.Headers.Should().NotContainKey(Headers.TenantId);
    }

    [Fact]
    public void should_use_publish_options_tenant_when_strict_tenancy_enabled()
    {
        // given
        var factory = _CreateFactory(tenantContextRequired: true, ambientTenantId: null);

        // when
        var prepared = factory.Create(new TestMessage("v"), new PublishOptions { TenantId = "acme" });

        // then
        prepared.Message.Headers[Headers.TenantId].Should().Be("acme");
    }

    [Fact]
    public void should_resolve_ambient_tenant_when_publish_options_unset_and_strict_tenancy_enabled()
    {
        // given
        var factory = _CreateFactory(tenantContextRequired: true, ambientTenantId: "acme");

        // when
        var prepared = factory.Create(new TestMessage("v"), options: null);

        // then
        prepared.Message.Headers[Headers.TenantId].Should().Be("acme");
    }

    [Fact]
    public void should_prefer_publish_options_tenant_over_ambient_when_both_set()
    {
        // given - publish-side explicit value wins; ambient is fallback only
        var factory = _CreateFactory(tenantContextRequired: true, ambientTenantId: "beta");

        // when
        var prepared = factory.Create(new TestMessage("v"), new PublishOptions { TenantId = "acme" });

        // then
        prepared.Message.Headers[Headers.TenantId].Should().Be("acme");
    }

    [Fact]
    public void should_throw_missing_tenant_context_when_strict_tenancy_enabled_and_both_null()
    {
        // given
        var factory = _CreateFactory(tenantContextRequired: true, ambientTenantId: null);

        // when
        var act = () => factory.Create(new TestMessage("v"), options: null);

        // then
        act.Should()
            .Throw<MissingTenantContextException>()
            .WithMessage("*Publish requires an ambient tenant context*")
            .WithMessage("*ICurrentTenant.Change*");
    }

    [Fact]
    public void should_not_attach_data_to_missing_tenant_context_exception()
    {
        // given
        var factory = _CreateFactory(tenantContextRequired: true, ambientTenantId: null);

        // when
        Action act = () => factory.Create(new TestMessage("v"), options: null);

        // then
        var exception = act.Should().Throw<MissingTenantContextException>().Which;
        exception.Data.Count.Should().Be(0);
    }

    [Fact]
    public void should_let_u2_reserved_header_check_fire_before_u10_when_raw_header_set_without_typed()
    {
        // given - U2 ReservedTenantHeader takes priority over U10 absence check
        var factory = _CreateFactory(tenantContextRequired: true, ambientTenantId: "acme");
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.TenantId] = "injected" };

        // when
        Action act = () => factory.Create(new TestMessage("v"), new PublishOptions { Headers = headers });

        // then
        var exception = act.Should().Throw<InvalidOperationException>().WithMessage("*reserved*").Which;
        exception.Data.Count.Should().Be(1);
        exception.Data["Headers.TenantId.Raw"].Should().Be("injected");
    }

    [Fact]
    public void should_let_u2_mismatch_check_fire_before_u10_when_typed_and_raw_disagree()
    {
        // given
        var factory = _CreateFactory(tenantContextRequired: true, ambientTenantId: null);
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.TenantId] = "wire-side" };

        // when
        Action act = () =>
            factory.Create(new TestMessage("v"), new PublishOptions { Headers = headers, TenantId = "explicit" });

        // then
        var exception = act.Should().Throw<InvalidOperationException>().WithMessage("*disagrees*").Which;
        exception.Data.Count.Should().Be(2);
        exception.Data[$"{nameof(PublishOptions)}.{nameof(PublishOptions.TenantId)}"].Should().Be("explicit");
        exception.Data["Headers.TenantId.Raw"].Should().Be("wire-side");
    }

    [Fact]
    public void should_resolve_ambient_tenant_set_via_change_for_background_worker_path()
    {
        // given - simulates an IHostedService that wraps publish in ICurrentTenant.Change(...)
        var accessor = AsyncLocalCurrentTenantAccessor.Instance;
        accessor.Current = null;
        var currentTenant = new CurrentTenant(accessor);
        var factory = _CreateFactory(tenantContextRequired: true, currentTenant: currentTenant);

        PreparedPublishMessage prepared;
        using (currentTenant.Change("acme"))
        {
            // when
            prepared = factory.Create(new TestMessage("v"), options: null);
        }

        // then
        prepared.Message.Headers[Headers.TenantId].Should().Be("acme");
    }

    [Fact]
    public void should_throw_for_background_worker_without_change_or_publish_options()
    {
        // given - background context with no Change scope and no PublishOptions.TenantId
        var accessor = AsyncLocalCurrentTenantAccessor.Instance;
        accessor.Current = null;
        var currentTenant = new CurrentTenant(accessor);
        var factory = _CreateFactory(tenantContextRequired: true, currentTenant: currentTenant);

        // when
        Action act = () => factory.Create(new TestMessage("v"), options: null);

        // then
        act.Should().Throw<MissingTenantContextException>();
    }

    private static MessagePublishRequestFactory _CreateFactory(
        bool tenantContextRequired,
        string? ambientTenantId = null,
        ICurrentTenant? currentTenant = null
    )
    {
        var options = new MessagingOptions { TenantContextRequired = tenantContextRequired };
        options.WithTopicMapping(typeof(TestMessage), "test.topic");

        var resolvedTenant = currentTenant ?? new StubCurrentTenant(ambientTenantId);

        return new MessagePublishRequestFactory(
            new SnowflakeIdLongIdGenerator(),
            TimeProvider.System,
            Options.Create(options),
            resolvedTenant
        );
    }

    private sealed class StubCurrentTenant(string? id) : ICurrentTenant
    {
        public bool IsAvailable => Id is not null;
        public string? Id { get; } = id;
        public string? Name => null;

        public IDisposable Change(string? id, string? name = null) =>
            throw new NotSupportedException("Stub does not support scope changes; use CurrentTenant for that.");
    }
}
