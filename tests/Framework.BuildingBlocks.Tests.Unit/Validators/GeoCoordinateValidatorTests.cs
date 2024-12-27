// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Validators;

namespace Tests.Validators;

public sealed class GeoCoordinateValidatorTests
{
    [Theory]
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    [InlineData(0, 0)]
    public void should_validate_coordinates_when_valid(double latitude, double longitude)
    {
        var result = GeoCoordinateValidator.IsValid(latitude, longitude);

        result.Should().BeTrue();
    }

    [Theory]
    // latitude is out of range
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    // longitude is out of range
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public void should_have_error_when_invalid_coordinates(double latitude, double longitude)
    {
        var result = GeoCoordinateValidator.IsValid(latitude, longitude);

        result.Should().BeFalse();
    }
}
