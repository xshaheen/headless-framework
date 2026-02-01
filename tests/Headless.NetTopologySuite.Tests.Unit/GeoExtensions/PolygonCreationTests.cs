// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite.Constants;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

namespace Tests.GeoExtensions;

public sealed class PolygonCreationTests
{
    private static GeometryFactory Factory => GeoConstants.GeometryFactory;

    // CCW coordinates (counterclockwise - going: right, up, left, down)
    private static Coordinate[] CcwCoords =>
    [
        new(0, 0), new(1, 0), new(1, 1), new(0, 1), new(0, 0),
    ];

    // CW coordinates (clockwise - going: up, right, down, left - opposite of CCW)
    private static Coordinate[] CwCoords =>
    [
        new(0, 0), new(0, 1), new(1, 1), new(1, 0), new(0, 0),
    ];

    [Fact]
    public void CreatePolygon_from_points_should_create_valid_polygon()
    {
        var points = CcwCoords.Select(c => Factory.CreatePoint(c)).ToArray();

        var polygon = Factory.CreatePolygon(points);

        polygon.IsValid.Should().BeTrue();
        polygon.Shell.NumPoints.Should().Be(5);
    }

    [Fact]
    public void CreatePolygon_from_points_should_ensure_ccw()
    {
        var points = CwCoords.Select(c => Factory.CreatePoint(c)).ToArray();

        var polygon = Factory.CreatePolygon(points);

        Orientation.IsCCW(polygon.Shell.CoordinateSequence).Should().BeTrue();
    }

    [Fact]
    public void CreatePolygon_from_coordinates_should_create_valid_polygon()
    {
        var polygon = Factory.CreatePolygon((IEnumerable<Coordinate>)CcwCoords);

        polygon.IsValid.Should().BeTrue();
        polygon.Shell.NumPoints.Should().Be(5);
    }

    [Fact]
    public void CreatePolygon_from_coordinates_should_ensure_ccw()
    {
        var polygon = Factory.CreatePolygon((IEnumerable<Coordinate>)CwCoords);

        Orientation.IsCCW(polygon.Shell.CoordinateSequence).Should().BeTrue();
    }

    [Fact]
    public void CreateMultiPolygon_should_create_from_coordinate_arrays()
    {
        Coordinate[][] coordArrays =
        [
            CcwCoords,
            [new(2, 2), new(3, 2), new(3, 3), new(2, 3), new(2, 2)],
        ];

        var multiPolygon = Factory.CreateMultiPolygon(coordArrays);

        multiPolygon.NumGeometries.Should().Be(2);
        multiPolygon.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateMultiPolygon_should_ensure_all_ccw()
    {
        Coordinate[][] coordArrays =
        [
            CwCoords, // clockwise - should be reversed
            [new(2, 2), new(2, 3), new(3, 3), new(3, 2), new(2, 2)], // clockwise
        ];

        var multiPolygon = Factory.CreateMultiPolygon(coordArrays);

        foreach (var geom in multiPolygon.Geometries)
        {
            var polygon = (Polygon)geom;
            Orientation.IsCCW(polygon.Shell.CoordinateSequence).Should().BeTrue();
        }
    }
}
