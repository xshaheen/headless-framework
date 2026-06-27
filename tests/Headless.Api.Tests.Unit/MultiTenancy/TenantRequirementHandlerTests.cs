// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Api.MultiTenancy;
using Headless.Testing.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Tests.MultiTenancy;

public sealed class TenantRequirementHandlerTests
{
    [Fact]
    public async Task should_succeed_when_current_tenant_id_is_available()
    {
        // given
        var requirement = new TenantRequirement();
        var context = _CreateContext(requirement, new DefaultHttpContext());
        var handler = new TenantRequirementHandler(new TestCurrentTenant { Id = "tenant-a" });

        // when
        await handler.HandleAsync(context);

        // then
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_succeed_when_endpoint_allows_missing_tenant()
    {
        // given
        var requirement = new TenantRequirement();
        var httpContext = new DefaultHttpContext();
        httpContext.SetEndpoint(
            new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new AllowMissingTenantAttribute()),
                displayName: "allowed"
            )
        );
        var context = _CreateContext(requirement, httpContext);
        var handler = new TenantRequirementHandler(new TestCurrentTenant());

        // when
        await handler.HandleAsync(context);

        // then
        context.HasSucceeded.Should().BeTrue();
        httpContext.Features.Get<TenantContextRequiredFeature>().Should().BeNull();
    }

    [Fact]
    public async Task should_fail_when_endpoint_requires_tenant_after_allowing_missing_tenant()
    {
        // given
        var requirement = new TenantRequirement();
        var httpContext = new DefaultHttpContext();
        httpContext.SetEndpoint(
            new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new AllowMissingTenantAttribute(), new RequireTenantAttribute()),
                displayName: "required"
            )
        );
        var context = _CreateContext(requirement, httpContext);
        var handler = new TenantRequirementHandler(new TestCurrentTenant());

        // when
        await handler.HandleAsync(context);

        // then
        context.HasFailed.Should().BeTrue();
        context.FailureReasons.Should().ContainSingle(reason => reason.Message == TenantRequirement.FailureReason);
    }

    [Fact]
    public async Task should_succeed_when_endpoint_allows_missing_tenant_after_requiring_tenant()
    {
        // given
        var requirement = new TenantRequirement();
        var httpContext = new DefaultHttpContext();
        httpContext.SetEndpoint(
            new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new RequireTenantAttribute(), new AllowMissingTenantAttribute()),
                displayName: "allowed"
            )
        );
        var context = _CreateContext(requirement, httpContext);
        var handler = new TenantRequirementHandler(new TestCurrentTenant());

        // when
        await handler.HandleAsync(context);

        // then
        context.HasSucceeded.Should().BeTrue();
        httpContext.Features.Get<TenantContextRequiredFeature>().Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_fail_with_tenant_reason_when_tenant_missing_and_endpoint_does_not_opt_out(string? tenantId)
    {
        // given
        var requirement = new TenantRequirement();
        var context = _CreateContext(requirement, new DefaultHttpContext());
        var handler = new TenantRequirementHandler(new TestCurrentTenant { Id = tenantId });

        // when
        await handler.HandleAsync(context);

        // then
        context.HasFailed.Should().BeTrue();
        context.PendingRequirements.Should().Contain(requirement);
        context.FailureReasons.Should().ContainSingle(reason => reason.Message == TenantRequirement.FailureReason);
    }

    [Fact]
    public async Task should_set_typed_feature_on_http_context_when_failing()
    {
        // given - StatusCodesRewriterMiddleware reads this typed feature to enrich the bare 403
        // with the g:tenant_required discriminator; without it, the response degrades to the
        // generic 403 ProblemDetails body. Asserting the feature here guards the contract between
        // the handler and the rewriter middleware.
        var requirement = new TenantRequirement();
        var httpContext = new DefaultHttpContext();
        var context = _CreateContext(requirement, httpContext);
        var handler = new TenantRequirementHandler(new TestCurrentTenant());

        // when
        await handler.HandleAsync(context);

        // then
        context.HasFailed.Should().BeTrue();
        httpContext.Features.Get<TenantContextRequiredFeature>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_not_set_typed_feature_when_succeeding()
    {
        // given
        var requirement = new TenantRequirement();
        var httpContext = new DefaultHttpContext();
        var context = _CreateContext(requirement, httpContext);
        var handler = new TenantRequirementHandler(new TestCurrentTenant { Id = "tenant-a" });

        // when
        await handler.HandleAsync(context);

        // then
        context.HasSucceeded.Should().BeTrue();
        httpContext.Features.Get<TenantContextRequiredFeature>().Should().BeNull();
    }

    [Fact]
    public async Task should_not_throw_when_resource_is_not_http_context()
    {
        // given
        var requirement = new TenantRequirement();
        var context = new AuthorizationHandlerContext([requirement], _CreatePrincipal(), resource: new object());
        var handler = new TenantRequirementHandler(new TestCurrentTenant());

        // when
        var act = () => handler.HandleAsync(context);

        // then
        await act.Should().NotThrowAsync();
        context.HasFailed.Should().BeTrue();
    }

    private static AuthorizationHandlerContext _CreateContext(TenantRequirement requirement, HttpContext httpContext)
    {
        return new AuthorizationHandlerContext([requirement], _CreatePrincipal(), resource: httpContext);
    }

    private static ClaimsPrincipal _CreatePrincipal()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }
}
