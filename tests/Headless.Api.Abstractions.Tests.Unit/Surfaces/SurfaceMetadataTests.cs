// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Surfaces;
using Headless.Testing.Tests;

namespace Tests.Surfaces;

public sealed class SurfaceMetadataTests : TestBase
{
    [Fact]
    public void should_construct_api_surface_feature()
    {
        var feature = new ApiSurfaceFeature(
            surfaceName: "Portal",
            routePrefix: "api/portal",
            requiredPolicy: "PortalUser",
            tenancyPosture: SurfaceTenancyPosture.RequireTenant
        );

        feature.SurfaceName.Should().Be("Portal");
        feature.RoutePrefix.Should().Be("api/portal");
        feature.RequiredPolicy.Should().Be("PortalUser");
        feature.TenancyPosture.Should().Be(SurfaceTenancyPosture.RequireTenant);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_invalid_surface_name(string? invalidName)
    {
        var act = () => new ApiSurfaceFeature(invalidName!);

        act.Should().Throw<ArgumentException>();
    }
}
