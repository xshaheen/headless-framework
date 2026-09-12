// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Api.Surfaces;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Tests.Surfaces;

public sealed class ApiSurfaceMiddlewareTests : TestBase
{
    private sealed class StubSurfaceMetadata(string surfaceName) : IApiSurfaceMetadata
    {
        public string SurfaceName { get; } = surfaceName;
    }

    [Fact]
    public async Task should_tag_unknown_when_no_endpoint_mapped()
    {
        var options = Options.Create(new ApiSurfaceOptions());
        var middleware = new ApiSurfaceMiddleware(_ => Task.CompletedTask, options);
        var context = new DefaultHttpContext();

        using var activity = new Activity("test").Start();

        await middleware.InvokeAsync(context);

        activity.GetTagItem("api.surface").Should().Be("unknown");
        context.Features.Get<ApiSurfaceFeature>().Should().BeNull();
    }

    [Fact]
    public async Task should_tag_infrastructure_when_endpoint_has_no_surface_metadata()
    {
        var options = Options.Create(new ApiSurfaceOptions());
        var middleware = new ApiSurfaceMiddleware(_ => Task.CompletedTask, options);
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "infra"));

        using var activity = new Activity("test").Start();

        await middleware.InvokeAsync(context);

        activity.GetTagItem("api.surface").Should().Be("infrastructure");
        context.Features.Get<ApiSurfaceFeature>().Should().BeNull();
    }

    [Fact]
    public async Task should_attach_feature_and_tag_activity_for_configured_surface()
    {
        var surfaceOptions = new ApiSurfaceOptions();
        surfaceOptions.AddSurface(
            "Portal",
            s =>
            {
                s.RoutePrefix = "api/portal";
                s.RequiredPolicy = "PortalUser";
                s.TenancyPosture = SurfaceTenancyPosture.RequireTenant;
            }
        );

        var options = Options.Create(surfaceOptions);
        var middleware = new ApiSurfaceMiddleware(_ => Task.CompletedTask, options);
        var context = new DefaultHttpContext();
        var metadata = new EndpointMetadataCollection(new StubSurfaceMetadata("Portal"));
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "portal-endpoint"));

        using var activity = new Activity("test").Start();

        await middleware.InvokeAsync(context);

        activity.GetTagItem("api.surface").Should().Be("Portal");
        var feature = context.Features.Get<ApiSurfaceFeature>();
        feature.Should().NotBeNull();
        feature!.SurfaceName.Should().Be("Portal");
        feature.RoutePrefix.Should().Be("api/portal");
        feature.RequiredPolicy.Should().Be("PortalUser");
        feature.TenancyPosture.Should().Be(SurfaceTenancyPosture.RequireTenant);
    }
}
