// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Validators;

namespace Tests.Validators;

public sealed class GeoCoordinateValidatorTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(30.0444, 31.2357)] // Cairo
    [InlineData(40.7128, -74.0060)] // New York
    [InlineData(-33.8688, 151.2093)] // Sydney
    public void should_return_true_for_valid_coordinates(double latitude, double longitude)
    {
        var result = GeoCoordinateValidator.IsValid(latitude, longitude);
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_boundary_latitude_min()
    {
        var result = GeoCoordinateValidator.IsValidLatitude(GeoCoordinateValidator.LatitudeMinValue);
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_boundary_latitude_max()
    {
        var result = GeoCoordinateValidator.IsValidLatitude(GeoCoordinateValidator.LatitudeMaxValue);
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_boundary_longitude_min()
    {
        var result = GeoCoordinateValidator.IsValidLongitude(GeoCoordinateValidator.LongitudeMinValue);
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_boundary_longitude_max()
    {
        var result = GeoCoordinateValidator.IsValidLongitude(GeoCoordinateValidator.LongitudeMaxValue);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(-90.1)]
    [InlineData(-100)]
    [InlineData(-1000)]
    public void should_return_false_for_latitude_below_min(double latitude)
    {
        var result = GeoCoordinateValidator.IsValidLatitude(latitude);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(90.1)]
    [InlineData(100)]
    [InlineData(1000)]
    public void should_return_false_for_latitude_above_max(double latitude)
    {
        var result = GeoCoordinateValidator.IsValidLatitude(latitude);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(-180.1)]
    [InlineData(-200)]
    [InlineData(-1000)]
    public void should_return_false_for_longitude_below_min(double longitude)
    {
        var result = GeoCoordinateValidator.IsValidLongitude(longitude);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(180.1)]
    [InlineData(200)]
    [InlineData(1000)]
    public void should_return_false_for_longitude_above_max(double longitude)
    {
        var result = GeoCoordinateValidator.IsValidLongitude(longitude);
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_NaN_latitude()
    {
        var result = GeoCoordinateValidator.IsValidLatitude(double.NaN);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void should_return_false_for_Infinity_longitude(double longitude)
    {
        var result = GeoCoordinateValidator.IsValidLongitude(longitude);
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_NaN_longitude()
    {
        var result = GeoCoordinateValidator.IsValidLongitude(double.NaN);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void should_return_false_for_Infinity_latitude(double latitude)
    {
        var result = GeoCoordinateValidator.IsValidLatitude(latitude);
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_invalid_latitude_in_combined_check()
    {
        var result = GeoCoordinateValidator.IsValid(latitude: 100, longitude: 0);
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_invalid_longitude_in_combined_check()
    {
        var result = GeoCoordinateValidator.IsValid(latitude: 0, longitude: 200);
        result.Should().BeFalse();
    }
}
