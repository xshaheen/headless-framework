// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Surfaces;
using Headless.Testing.Tests;

namespace Tests.Surfaces;

public sealed class SurfaceMetadataTests : TestBase
{
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
