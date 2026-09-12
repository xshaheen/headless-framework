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
                    s.RequiredPolicy = "PortalAdmin";
                    s.GroupName = "PortalGroup";
                    s.TenancyPosture = SurfaceTenancyPosture.RequireTenant;
                }
            );
        });

        var sp = services.BuildServiceProvider();
        var app = new DefaultEndpointRouteBuilder(sp);

        var group = app.MapSurfaceGroup(
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

    private sealed class DefaultEndpointRouteBuilder(IServiceProvider sp) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = sp;
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IApplicationBuilder CreateApplicationBuilder() => throw new NotSupportedException();
    }
}
