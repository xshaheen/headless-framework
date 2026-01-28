// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable CA1508 // Avoid dead conditional code - intentional for null-handling tests

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class GeoCoordinateTests
{
    #region Construction & Properties

    [Fact]
    public void should_store_latitude_and_longitude()
    {
        // when
        var coord = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };

        // then
        coord.Latitude.Should().Be(40.7128);
        coord.Longitude.Should().Be(-74.0060);
    }

    [Fact]
    public void should_store_zero_coordinates()
    {
        // when
        var coord = new GeoCoordinate { Latitude = 0, Longitude = 0 };

        // then
        coord.Latitude.Should().Be(0);
        coord.Longitude.Should().Be(0);
    }

    [Fact]
    public void should_store_negative_coordinates()
    {
        // when
        var coord = new GeoCoordinate { Latitude = -33.8688, Longitude = -151.2093 };

        // then
        coord.Latitude.Should().Be(-33.8688);
        coord.Longitude.Should().Be(-151.2093);
    }

    [Fact]
    public void should_store_extreme_latitude_values()
    {
        // when
        var northPole = new GeoCoordinate { Latitude = 90, Longitude = 0 };
        var southPole = new GeoCoordinate { Latitude = -90, Longitude = 0 };

        // then
        northPole.Latitude.Should().Be(90);
        southPole.Latitude.Should().Be(-90);
    }

    [Fact]
    public void should_store_extreme_longitude_values()
    {
        // when
        var east = new GeoCoordinate { Latitude = 0, Longitude = 180 };
        var west = new GeoCoordinate { Latitude = 0, Longitude = -180 };

        // then
        east.Longitude.Should().Be(180);
        west.Longitude.Should().Be(-180);
    }

    #endregion

    #region Equality

    [Fact]
    public void equals_should_return_true_for_same_coordinates()
    {
        // given
        var coord1 = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };
        var coord2 = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };

        // then
        coord1.Equals(coord2).Should().BeTrue();
        (coord1 == coord2).Should().BeTrue();
        (coord1 != coord2).Should().BeFalse();
    }

    [Fact]
    public void equals_should_return_false_for_different_latitude()
    {
        // given
        var coord1 = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };
        var coord2 = new GeoCoordinate { Latitude = 41.0000, Longitude = -74.0060 };

        // then
        coord1.Equals(coord2).Should().BeFalse();
        (coord1 == coord2).Should().BeFalse();
        (coord1 != coord2).Should().BeTrue();
    }

    [Fact]
    public void equals_should_return_false_for_different_longitude()
    {
        // given
        var coord1 = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };
        var coord2 = new GeoCoordinate { Latitude = 40.7128, Longitude = -75.0000 };

        // then
        coord1.Equals(coord2).Should().BeFalse();
    }

    [Fact]
    public void equals_should_return_false_for_null()
    {
        // given
        var coord = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };

        // then
        coord.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void equals_should_return_true_for_same_reference()
    {
        // given
        var coord = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };

        // then
        coord.Equals(coord).Should().BeTrue();
    }

    [Fact]
    public void equals_object_should_return_false_for_different_type()
    {
        // given
        var coord = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };

        // then
        coord.Equals("not a coordinate").Should().BeFalse();
    }

    [Fact]
    public void equality_operator_should_handle_null_left()
    {
        // given
        GeoCoordinate? left = null;
        var right = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };

        // then
        (left == right)
            .Should()
            .BeFalse();
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void equality_operator_should_handle_null_right()
    {
        // given
        var left = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };
        GeoCoordinate? right = null;

        // then
        (left == right)
            .Should()
            .BeFalse();
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void equality_operator_should_handle_both_null()
    {
        // given
        GeoCoordinate? left = null;
        GeoCoordinate? right = null;

        // then
        (left == right)
            .Should()
            .BeTrue();
        (left != right).Should().BeFalse();
    }

    [Fact]
    public void get_hash_code_should_be_equal_for_equal_coordinates()
    {
        // given
        var coord1 = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };
        var coord2 = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };

        // then
        coord1.GetHashCode().Should().Be(coord2.GetHashCode());
    }

    [Fact]
    public void get_hash_code_should_differ_for_different_coordinates()
    {
        // given
        var coord1 = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };
        var coord2 = new GeoCoordinate { Latitude = 51.5074, Longitude = -0.1278 };

        // then
        coord1.GetHashCode().Should().NotBe(coord2.GetHashCode());
    }

    #endregion

    #region Formatting

    [Fact]
    public void to_string_should_format_coordinates()
    {
        // given
        var coord = new GeoCoordinate { Latitude = 40.7128, Longitude = -74.0060 };

        // when
        var result = coord.ToString();

        // then
        result.Should().Be("(lat=40.7128, long=-74.006)");
    }

    [Fact]
    public void to_string_should_format_zero_coordinates()
    {
        // given
        var coord = new GeoCoordinate { Latitude = 0, Longitude = 0 };

        // when
        var result = coord.ToString();

        // then
        result.Should().Be("(lat=0, long=0)");
    }

    [Fact]
    public void to_string_should_use_invariant_culture()
    {
        // given
        var coord = new GeoCoordinate { Latitude = 1.5, Longitude = 2.5 };

        // when
        var result = coord.ToString();

        // then
        result.Should().Contain("1.5");
        result.Should().Contain("2.5");
    }

    #endregion
}
