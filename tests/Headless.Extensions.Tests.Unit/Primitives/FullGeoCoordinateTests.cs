// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class FullGeoCoordinateTests
{
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
