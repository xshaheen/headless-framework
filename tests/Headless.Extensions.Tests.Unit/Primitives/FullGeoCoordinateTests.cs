// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class FullGeoCoordinateTests
{
    #region Construction

    [Fact]
    public void should_default_optional_components_to_nan_when_constructed_from_latitude_and_longitude()
    {
        // when
        var point = new FullGeoCoordinate(latitude: 30.0444, longitude: 31.2357);

        // then
        point.Altitude.Should().Be(double.NaN);
        point.HorizontalAccuracy.Should().Be(double.NaN);
        point.VerticalAccuracy.Should().Be(double.NaN);
        point.Speed.Should().Be(double.NaN);
        point.Course.Should().Be(double.NaN);
    }

    [Fact]
    public void should_set_optional_components_via_object_initializer()
    {
        // when
        var point = new FullGeoCoordinate(latitude: 30.0444, longitude: 31.2357)
        {
            Altitude = 22.5,
            HorizontalAccuracy = 5,
            VerticalAccuracy = 8,
            Speed = 1.5,
            Course = 90,
        };

        // then
        point.Altitude.Should().Be(22.5);
        point.HorizontalAccuracy.Should().Be(5);
        point.VerticalAccuracy.Should().Be(8);
        point.Speed.Should().Be(1.5);
        point.Course.Should().Be(90);
    }

    [Fact]
    public void should_treat_zero_accuracy_as_unknown_when_set_via_initializer()
    {
        // when - a 0 accuracy means "unknown" and normalizes to NaN (original GeoCoordinate semantics)
        var point = new FullGeoCoordinate(latitude: 1, longitude: 2) { HorizontalAccuracy = 0, VerticalAccuracy = 0 };

        // then
        point.HorizontalAccuracy.Should().Be(double.NaN);
        point.VerticalAccuracy.Should().Be(double.NaN);
    }

    [Fact]
    public void should_be_unknown_when_constructed_without_arguments()
    {
        new FullGeoCoordinate().IsUnknown.Should().BeTrue();
    }

    [Fact]
    public void should_throw_when_optional_component_is_out_of_range()
    {
        // then
        FluentActions
            .Invoking(() => new FullGeoCoordinate(latitude: 1, longitude: 2) { Course = 361 })
            .Should()
            .Throw<ArgumentOutOfRangeException>();

        FluentActions
            .Invoking(() => new FullGeoCoordinate(latitude: 1, longitude: 2) { Speed = -1 })
            .Should()
            .Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region GetHashCode

    [Fact]
    public void should_not_collide_for_swapped_coordinates_when_get_hash_code()
    {
        // given - the old XOR hash made (10, 20) and (20, 10) collide
        var a = new FullGeoCoordinate(latitude: 10, longitude: 20);
        var b = new FullGeoCoordinate(latitude: 20, longitude: 10);

        // then
        a.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    [Fact]
    public void should_be_equal_for_equal_coordinates_when_get_hash_code()
    {
        // given
        var a = new FullGeoCoordinate(latitude: 40.7128, longitude: -74.0060);
        var b = new FullGeoCoordinate(latitude: 40.7128, longitude: -74.0060);

        // then
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    #endregion

    #region GetDistanceTo

    [Fact]
    public void should_be_zero_for_the_same_point_when_get_distance_to()
    {
        // given
        var point = new FullGeoCoordinate(latitude: 30.0444, longitude: 31.2357);

        // when
        var distance = point.GetDistanceTo(point);

        // then
        distance.Should().BeApproximately(0, 1e-6);
    }

    [Fact]
    public void should_be_symmetric_and_positive_when_get_distance_to()
    {
        // given
        var cairo = new FullGeoCoordinate(latitude: 30.0444, longitude: 31.2357);
        var london = new FullGeoCoordinate(latitude: 51.5074, longitude: -0.1278);

        // when
        var forward = cairo.GetDistanceTo(london);
        var backward = london.GetDistanceTo(cairo);

        // then - ~3500 km between Cairo and London; the refactored sin(x)^2 keeps the math intact
        forward.Should().BeGreaterThan(3_000_000).And.BeLessThan(4_000_000);
        forward.Should().BeApproximately(backward, 1e-6);
    }

    #endregion
}
