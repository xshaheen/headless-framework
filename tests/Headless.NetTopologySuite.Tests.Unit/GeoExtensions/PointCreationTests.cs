// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite.Constants;
using NetTopologySuite.Geometries;

namespace Tests.GeoExtensions;

public sealed class PointCreationTests
{
    private static GeometryFactory Factory => GeoConstants.GeometryFactory;

    [Fact]
    public void CreatePoint_should_create_point_with_coordinates()
    {
        var point = Factory.CreatePoint(30.0, 31.0);

        point.X.Should().Be(30.0);
        point.Y.Should().Be(31.0);
    }

    [Fact]
    public void CreatePoint_should_preserve_factory_srid()
    {
        var point = Factory.CreatePoint(30.0, 31.0);

        point.SRID.Should().Be(GeoConstants.GoogleMapsSrid);
    }

    [Fact]
    public void ToCoordinates_should_convert_points_to_coordinate_array()
    {
        var point1 = Factory.CreatePoint(10.0, 20.0);
        var point2 = Factory.CreatePoint(30.0, 40.0);
        var points = new[] { point1, point2 };

        var coordinates = points.ToCoordinates();

        coordinates.Should().HaveCount(2);
        coordinates[0].X.Should().Be(10.0);
        coordinates[0].Y.Should().Be(20.0);
        coordinates[1].X.Should().Be(30.0);
        coordinates[1].Y.Should().Be(40.0);
    }

    [Fact]
    public void ToCoordinates_should_return_empty_for_empty_enumerable()
    {
        var points = Array.Empty<Point>();

        var coordinates = points.ToCoordinates();

        coordinates.Should().BeEmpty();
    }
}
