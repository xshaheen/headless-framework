// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Api;
using Headless.Api.Middlewares;
using Headless.Api.MultiTenancy;
using Headless.Constants;
using Headless.MultiTenancy;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.MultiTenancy;

/// <summary>
/// Pins tenant telemetry at both HTTP entry points: while the inner pipeline runs, the <c>TenantId</c> logging scope
/// is active and the request span carries <c>tenant.id</c>; once the middleware returns the scope is gone, while the
/// span keeps the tag because it is exported after the middleware returns.
/// </summary>
public sealed class TenantResolutionTelemetryTests : TestBase
{
    private static readonly KeyValuePair<string, object?> _TenantScopeProperty = new("TenantId", "TENANT-1");

    [Fact]
    public async Task should_enrich_logs_and_span_during_claim_resolved_request()
    {
        // given
        var logger = new ScopeRecordingLogger<TenantResolutionMiddleware>();
        var observed = new Observed();
        var middleware = new TenantResolutionMiddleware(
            _ => observed.Capture(logger),
            Options.Create(new MultiTenancyOptions { ClaimType = UserClaimTypes.TenantId }),
            Options.Create(new TenantTelemetryOptions()),
            logger
        );
        var context = _CreateContext(tenantClaim: "TENANT-1");
        using var request = RecordedTestActivity.Start();

        // when
        await middleware.InvokeAsync(context, new TestCurrentTenant());

        // then
        observed.ScopeProperties.Should().ContainSingle().Which.Should().Be(_TenantScopeProperty);
        observed.SpanTenant.Should().Be("TENANT-1");
        logger.GetActiveScopeProperties().Should().BeEmpty();
        request.Activity.GetTagItem("tenant.id").Should().Be("TENANT-1");
    }

    [Fact]
    public async Task should_not_enrich_when_request_has_no_tenant_claim()
    {
        // given
        var logger = new ScopeRecordingLogger<TenantResolutionMiddleware>();
        var observed = new Observed();
        var middleware = new TenantResolutionMiddleware(
            _ => observed.Capture(logger),
            Options.Create(new MultiTenancyOptions { ClaimType = UserClaimTypes.TenantId }),
            Options.Create(new TenantTelemetryOptions()),
            logger
        );
        using var request = RecordedTestActivity.Start();

        // when
        await middleware.InvokeAsync(_CreateContext(tenantClaim: null), new TestCurrentTenant());

        // then
        observed.ScopeProperties.Should().BeEmpty();
        observed.SpanTenant.Should().BeNull();
    }

    [Fact]
    public async Task should_enrich_logs_and_span_during_catalog_resolved_request()
    {
        // given
        var logger = new ScopeRecordingLogger<TenantCatalogResolutionMiddleware>();
        var observed = new Observed();
        var source = Substitute.For<ITenantIdentifierSource>();
        source.GetIdentifier(Arg.Any<HttpContext>()).Returns(TenantIdentifierSourceResult.Found("acme"));
        var catalog = Substitute.For<ITenantCatalogService>();
        catalog
            .ResolveAsync("acme", Arg.Any<CancellationToken>())
            .Returns(TenantResolutionOutcome.Resolved(new TenantInfo("TENANT-1", "acme", "Acme", isEnabled: true)));
        var middleware = new TenantCatalogResolutionMiddleware(
            _ => observed.Capture(logger),
            [source],
            Options.Create(new TenantCatalogOptions()),
            Options.Create(new MultiTenancyOptions { ClaimType = UserClaimTypes.TenantId }),
            Options.Create(new TenantTelemetryOptions()),
            logger
        );
        var context = _CreateContext(tenantClaim: null);
        using var request = RecordedTestActivity.Start();

        // when
        await middleware.InvokeAsync(context, new TestCurrentTenant(), catalog);

        // then
        observed.ScopeProperties.Should().ContainSingle().Which.Should().Be(_TenantScopeProperty);
        observed.SpanTenant.Should().Be("TENANT-1");
        logger.GetActiveScopeProperties().Should().BeEmpty();
        request.Activity.GetTagItem("tenant.id").Should().Be("TENANT-1");
    }

    [Fact]
    public async Task should_skip_disabled_channels()
    {
        // given
        var logger = new ScopeRecordingLogger<TenantResolutionMiddleware>();
        var observed = new Observed();
        var middleware = new TenantResolutionMiddleware(
            _ => observed.Capture(logger),
            Options.Create(new MultiTenancyOptions { ClaimType = UserClaimTypes.TenantId }),
            Options.Create(new TenantTelemetryOptions { EnrichLogs = false, EnrichTraces = false }),
            logger
        );
        using var request = RecordedTestActivity.Start();

        // when
        await middleware.InvokeAsync(_CreateContext(tenantClaim: "TENANT-1"), new TestCurrentTenant());

        // then
        observed.ScopeProperties.Should().BeEmpty();
        observed.SpanTenant.Should().BeNull();
    }

    private static DefaultHttpContext _CreateContext(string? tenantClaim)
    {
        List<Claim> claims = [new(UserClaimTypes.Name, "alice")];

        if (tenantClaim is not null)
        {
            claims.Add(new Claim(UserClaimTypes.TenantId, tenantClaim));
        }

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };
    }

    private sealed class Observed
    {
        public IReadOnlyList<KeyValuePair<string, object?>> ScopeProperties { get; private set; } = [];

        public object? SpanTenant { get; private set; }

        public Task Capture<T>(ScopeRecordingLogger<T> logger)
        {
            ScopeProperties = logger.GetActiveScopeProperties();
            SpanTenant = System.Diagnostics.Activity.Current?.GetTagItem("tenant.id");

            return Task.CompletedTask;
        }
    }
}
