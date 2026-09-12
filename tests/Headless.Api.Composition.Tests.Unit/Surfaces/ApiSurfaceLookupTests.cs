// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.Surfaces;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Surfaces;

public sealed class ApiSurfaceLookupTests : TestBase
{
    [Fact]
    public void should_follow_selected_endpoint_without_caching_request_state()
    {
        var services = new ServiceCollection();
        services.AddHeadlessApiSurfaces(options =>
        {
            options.AddSurface("Portal", surface => surface.AuthorizationPolicy = "PortalUser");
            options.AddSurface("console", _ => { });
        });
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.GetApiSurface().Should().BeNull();

        context.SetEndpoint(_Endpoint("portal"));
        var surface = context.GetApiSurface();
        surface.Should().BeSameAs(provider.GetRequiredService<ApiSurfaceRegistry>().GetRequiredSurface("Portal"));
        surface!.AuthorizationPolicy.Should().Be("PortalUser");

        context.SetEndpoint(_Endpoint("console"));
        context.GetApiSurface()!.SurfaceName.Should().Be("console");
        context.SetEndpoint(_Endpoint(null));
        context.GetApiSurface().Should().BeNull();
        context.SetEndpoint(null);
        context.GetApiSurface().Should().BeNull();
    }

    [Fact]
    public void should_reject_unregistered_surface()
    {
        var services = new ServiceCollection();
        services.AddHeadlessApiSurfaces(_ => { });
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.SetEndpoint(_Endpoint("missing"));

        var act = () => context.GetApiSurface();
        act.Should().Throw<InvalidOperationException>().WithMessage("*missing*AddHeadlessApiSurfaces*");
    }

    [Fact]
    public void should_allow_unmarked_endpoints_without_surface_services()
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(_Endpoint(null));
        context.GetApiSurface().Should().BeNull();
    }

    private static Endpoint _Endpoint(string? surfaceName) =>
        new(
            _ => Task.CompletedTask,
            surfaceName is null
                ? new EndpointMetadataCollection()
                : new EndpointMetadataCollection(new SurfaceMetadata(surfaceName)),
            "test"
        );

    private sealed record SurfaceMetadata(string SurfaceName) : IApiSurfaceMetadata;
}
