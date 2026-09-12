// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Surfaces;
using Headless.Testing.Tests;

namespace Tests.Surfaces;

public sealed class SurfaceMetadataTests : TestBase
{
    [Fact]
    public void should_expose_configured_surface_defaults()
    {
        var surface = new ApiSurfaceDescriptor(
            "Portal",
            "api/portal",
            "PortalUser",
            ApiSurfaceTenancyMode.RequireTenant,
            new ApiSurfaceOpenApiDescriptor("portal", "Portal API")
        );
        var feature = new ApiSurfaceFeature(surface);
        feature.SurfaceName.Should().Be("Portal");
        feature.Surface.Should().BeSameAs(surface);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_invalid_surface_name(string? invalidName)
    {
        var act = () =>
            new ApiSurfaceDescriptor(
                invalidName!,
                null,
                null,
                ApiSurfaceTenancyMode.Unspecified,
                new ApiSurfaceOpenApiDescriptor("portal", "Portal API")
            );
        act.Should().Throw<ArgumentException>();
    }
}
