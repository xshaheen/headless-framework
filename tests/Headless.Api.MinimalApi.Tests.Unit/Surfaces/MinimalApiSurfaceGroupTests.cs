// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.Api.Surfaces;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Surfaces;

public sealed class MinimalApiSurfaceGroupTests : TestBase
{
    [Fact]
    public void should_configure_route_group_with_surface_metadata_prefix_and_policy()
    {
        var services = new ServiceCollection();
        services.AddHeadlessApiSurfaces(options =>
        {
            options.AddSurface(
                "Portal",
                s =>
                {
                    s.RoutePrefix = "api/portal";
                    s.AuthorizationPolicy = "PortalAdmin";
                    s.TenancyMode = ApiSurfaceTenancyMode.RequireTenant;
                }
            );
        });

        using var sp = services.BuildServiceProvider();
        var app = new DefaultEndpointRouteBuilder(sp);

        var group = app.MapApiSurface(
            "Portal",
            portalGroup =>
            {
                portalGroup.MapGet("/users", () => "users");
            }
        );

        var dataSource = app.DataSources.Single();
        var endpoint = dataSource.Endpoints.Single();

        endpoint.DisplayName.Should().Contain("api/portal/users");

        var surfaceMeta = endpoint.Metadata.GetMetadata<IApiSurfaceMetadata>();
        surfaceMeta.Should().NotBeNull();
        surfaceMeta!.SurfaceName.Should().Be("Portal");

        var authMeta = endpoint.Metadata.OfType<IAuthorizeData>().ToList();
        authMeta.Should().ContainSingle(a => a.Policy == "PortalAdmin");

        var tenantMeta = endpoint.Metadata.GetMetadata<RequireTenantAttribute>();
        tenantMeta.Should().NotBeNull();
    }

    [Theory]
    [InlineData(ApiSurfaceTenancyMode.RequireTenant, false)]
    [InlineData(ApiSurfaceTenancyMode.AllowMissingTenant, true)]
    [InlineData(ApiSurfaceTenancyMode.SkipTenantResolution, true)]
    public void should_honor_endpoint_tenancy_overrides(ApiSurfaceTenancyMode mode, bool require)
    {
        var services = new ServiceCollection();
        services.AddHeadlessApiSurfaces(options => options.AddSurface("portal", surface => surface.TenancyMode = mode));
        using var provider = services.BuildServiceProvider();
        var app = new DefaultEndpointRouteBuilder(provider);
        var endpoint = app.MapApiSurface("portal").MapGet("/test", () => "test");
        if (require)
        {
            endpoint.RequireTenant();
        }
        else
        {
            endpoint.AllowMissingTenant();
        }

        var metadata = app.DataSources.Single().Endpoints.Single().Metadata;
        metadata.GetMetadata<SkipTenantResolutionAttribute>().Should().BeNull();
        (metadata.Last(x => x is RequireTenantAttribute or AllowMissingTenantAttribute) is RequireTenantAttribute)
            .Should()
            .Be(require);
    }

    [Fact]
    public void should_reject_unknown_and_conflicting_surfaces()
    {
        var services = new ServiceCollection();
        services.AddHeadlessApiSurfaces(options => options.AddSurface("portal").AddSurface("console"));
        using var provider = services.BuildServiceProvider();
        var app = new DefaultEndpointRouteBuilder(provider);
        var unknown = () => app.MapApiSurface("typo");
        unknown.Should().Throw<InvalidOperationException>().WithMessage("*typo*");
        app.MapApiSurface("portal").MapApiSurface("console").MapGet("/test", () => "test");
        var conflicting = () => app.DataSources.Single().Endpoints;
        conflicting.Should().Throw<InvalidOperationException>().WithMessage("*conflicting*");
    }

    private sealed class DefaultEndpointRouteBuilder(IServiceProvider sp) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = sp;
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IApplicationBuilder CreateApplicationBuilder() => throw new NotSupportedException();
    }
}
