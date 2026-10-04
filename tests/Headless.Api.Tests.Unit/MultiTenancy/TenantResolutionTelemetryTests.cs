// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Security.Claims;
using Headless.Api;
using Headless.MultiTenancy;
using Headless.Security;
using Headless.Testing;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.MultiTenancy;

/// <summary>
/// Pins the request-span tag at both HTTP entry points. The request span starts before the tenant is resolved, so the
/// telemetry pipeline cannot tag it at start; the middleware tags the span the host recorded for the request.
/// </summary>
public sealed class TenantResolutionTelemetryTests : TestBase
{
    [Fact]
    public async Task should_tag_request_span_for_claim_resolved_request()
    {
        // given
        using var request = RecordedTestActivity.Start();
        var middleware = _CreateClaimMiddleware(new TenantTelemetryOptions());

        // when
        await middleware.InvokeAsync(_CreateContext("TENANT-1", request.Activity), new TestCurrentTenant());

        // then
        request.Activity.GetTagItem("tenant.id").Should().Be("TENANT-1");
    }

    [Fact]
    public async Task should_tag_request_span_rather_than_current_child_span()
    {
        // given
        using var request = RecordedTestActivity.Start();
        using var child = new Activity("child");
        child.Start();
        var middleware = _CreateClaimMiddleware(new TenantTelemetryOptions());

        // when
        await middleware.InvokeAsync(_CreateContext("TENANT-1", request.Activity), new TestCurrentTenant());

        // then
        request.Activity.GetTagItem("tenant.id").Should().Be("TENANT-1");
        child.GetTagItem("tenant.id").Should().BeNull();
    }

    [Fact]
    public async Task should_not_tag_when_request_has_no_tenant_claim()
    {
        // given
        using var request = RecordedTestActivity.Start();
        var middleware = _CreateClaimMiddleware(new TenantTelemetryOptions());

        // when
        await middleware.InvokeAsync(_CreateContext(tenantClaim: null, request.Activity), new TestCurrentTenant());

        // then
        request.Activity.GetTagItem("tenant.id").Should().BeNull();
    }

    [Fact]
    public async Task should_not_tag_when_traces_disabled()
    {
        // given
        using var request = RecordedTestActivity.Start();
        var middleware = _CreateClaimMiddleware(new TenantTelemetryOptions { EnrichTraces = false });

        // when
        await middleware.InvokeAsync(_CreateContext("TENANT-1", request.Activity), new TestCurrentTenant());

        // then
        request.Activity.GetTagItem("tenant.id").Should().BeNull();
    }

    [Fact]
    public async Task should_tag_request_span_for_catalog_resolved_request()
    {
        // given
        using var request = RecordedTestActivity.Start();
        var source = Substitute.For<ITenantIdentifierSource>();
        source.GetIdentifier(Arg.Any<HttpContext>()).Returns(TenantIdentifierSourceResult.Found("acme"));
        var catalog = Substitute.For<ITenantCatalogService>();
        catalog
            .ResolveAsync("acme", Arg.Any<CancellationToken>())
            .Returns(TenantResolutionOutcome.Resolved(new TenantInfo("TENANT-1", "acme", "Acme", isEnabled: true)));
        var middleware = new TenantCatalogResolutionMiddleware(
            _ => Task.CompletedTask,
            [source],
            Options.Create(new TenantCatalogOptions()),
            Options.Create(new MultiTenancyOptions { ClaimType = UserClaimTypes.TenantId }),
            Options.Create(new TenantTelemetryOptions()),
            NullLogger<TenantCatalogResolutionMiddleware>.Instance
        );

        // when
        await middleware.InvokeAsync(
            _CreateContext(tenantClaim: null, request.Activity),
            new TestCurrentTenant(),
            catalog
        );

        // then
        request.Activity.GetTagItem("tenant.id").Should().Be("TENANT-1");
    }

    private static TenantResolutionMiddleware _CreateClaimMiddleware(TenantTelemetryOptions telemetry)
    {
        return new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            Options.Create(new MultiTenancyOptions { ClaimType = UserClaimTypes.TenantId }),
            Options.Create(telemetry),
            NullLogger<TenantResolutionMiddleware>.Instance
        );
    }

    private static DefaultHttpContext _CreateContext(string? tenantClaim, Activity requestActivity)
    {
        List<Claim> claims = [new(UserClaimTypes.Name, "alice")];

        if (tenantClaim is not null)
        {
            claims.Add(new Claim(UserClaimTypes.TenantId, tenantClaim));
        }

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };
        context.Features.Set<IHttpActivityFeature>(new RequestActivityFeature(requestActivity));

        return context;
    }

    private sealed class RequestActivityFeature(Activity activity) : IHttpActivityFeature
    {
        public Activity Activity { get; set; } = activity;
    }
}
