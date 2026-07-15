// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite;
using NetTopologySuite.Geometries;

namespace Tests.GeoExtensions;

public sealed class UtilityTests
{
    private static GeometryFactory Factory => GeoServices.GeometryFactory;

    private static Polygon _CreateSquare()
    {
        var coords = new[]
        {
            new Coordinate(0, 0),
            new Coordinate(1, 0),
            new Coordinate(1, 1),
            new Coordinate(0, 1),
            new Coordinate(0, 0),
        };
        return Factory.CreatePolygon(coords);
    }

    #region ContainsEmpties

    [Fact]
    public void should_return_true_for_empty_geometry_when_contains_empties()
    {
        var empty = Factory.CreatePolygon();

        var result = empty.ContainsEmpties();

        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_linestring_when_contains_empties()
    {
        var lineString = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);

        var result = lineString.ContainsEmpties();

        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_point_when_contains_empties()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));

        var result = point.ContainsEmpties();

        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_collection_with_empty_when_contains_empties()
    {
        var emptyPolygon = Factory.CreatePolygon();
        var collection = Factory.CreateGeometryCollection([emptyPolygon]);

        var result = collection.ContainsEmpties();

        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_collection_with_point_when_contains_empties()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));
        var collection = Factory.CreateGeometryCollection([point]);

        var result = collection.ContainsEmpties();

        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_collection_with_linestring_when_contains_empties()
    {
        var lineString = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);
        var collection = Factory.CreateGeometryCollection([lineString]);

        var result = collection.ContainsEmpties();

        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_false_for_valid_polygon_when_contains_empties()
    {
        var polygon = _CreateSquare();

        var result = polygon.ContainsEmpties();

        result.Should().BeFalse();
    }

    #endregion

    #region CreateExpandBy

    [Fact]
    public void should_expand_envelope_when_create_expand_by()
    {
        var envelope = new Envelope(0, 10, 0, 10);

        var result = envelope.CreateExpandBy(5);

        result.MinX.Should().Be(-5);
        result.MaxX.Should().Be(15);
        result.MinY.Should().Be(-5);
        result.MaxY.Should().Be(15);
    }

    [Fact]
    public void should_expand_all_directions_when_create_expand_by()
    {
        var envelope = new Envelope(5, 10, 5, 10);
        const double factor = 2.0;

        var result = envelope.CreateExpandBy(factor);

        result.MinX.Should().Be(envelope.MinX - factor);
        result.MaxX.Should().Be(envelope.MaxX + factor);
        result.MinY.Should().Be(envelope.MinY - factor);
        result.MaxY.Should().Be(envelope.MaxY + factor);
    }

    #endregion

    #region Flatten

    [Fact]
    public void should_flatten_geometry_collection_when_flatten()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));
        var lineString = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);
        var innerCollection = Factory.CreateGeometryCollection([point]);
        var outerCollection = Factory.CreateGeometryCollection([innerCollection, lineString]);

        var result = outerCollection.Flatten();

        result.Should().HaveCount(2);
        result.Should().Contain(point);
        result.Should().Contain(lineString);
    }

    [Fact]
    public void should_return_single_geometry_as_array_when_flatten()
    {
        var polygon = _CreateSquare();

        var result = polygon.Flatten();

        result.Should().ContainSingle();
        result[0].Should().Be(polygon);
    }

    #endregion

    #region GetPolygonsOrEmpty

    [Fact]
    public void should_return_polygon_when_get_polygons_or_empty()
    {
        var polygon = _CreateSquare();

        var result = polygon.GetPolygonsOrEmpty();

        result.Should().ContainSingle();
        result[0].Should().Be(polygon);
    }

    [Fact]
    public void should_extract_from_collection_when_get_polygons_or_empty()
    {
        var polygon1 = _CreateSquare();
        var polygon2 = _CreateSquare();
        var point = Factory.CreatePoint(new Coordinate(0, 0));
        var innerCollection = Factory.CreateGeometryCollection([polygon1, point]);
        var outerCollection = Factory.CreateGeometryCollection([innerCollection, polygon2]);

        var result = outerCollection.GetPolygonsOrEmpty();

        result.Should().HaveCount(2);
        result.Should().Contain(polygon1);
        result.Should().Contain(polygon2);
    }

    [Fact]
    public void should_return_empty_for_point_when_get_polygons_or_empty()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));

        var result = point.GetPolygonsOrEmpty();

        result.Should().BeEmpty();
    }

    #endregion

    #region GetSimpleGeometryOrEmpty

    [Fact]
    public void should_return_point_when_get_simple_geometry_or_empty()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));

        var result = point.GetSimpleGeometryOrEmpty();

        result.Should().ContainSingle();
        result[0].Should().Be(point);
    }

    [Fact]
    public void should_return_linestring_when_get_simple_geometry_or_empty()
    {
        var lineString = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);

        var result = lineString.GetSimpleGeometryOrEmpty();

        result.Should().ContainSingle();
        result[0].Should().Be(lineString);
    }

    [Fact]
    public void should_extract_from_collection_when_get_simple_geometry_or_empty()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));
        var lineString = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);
        var polygon = _CreateSquare();
        var innerCollection = Factory.CreateGeometryCollection([point, polygon]);
        var outerCollection = Factory.CreateGeometryCollection([innerCollection, lineString]);

        var result = outerCollection.GetSimpleGeometryOrEmpty();

        result.Should().HaveCount(2);
        result.Should().Contain(point);
        result.Should().Contain(lineString);
    }

    [Fact]
    public void should_return_empty_for_polygon_when_get_simple_geometry_or_empty()
    {
        var polygon = _CreateSquare();

        var result = polygon.GetSimpleGeometryOrEmpty();

        result.Should().BeEmpty();
    }

    #endregion

    #region IsPolygonLikeGeometry

    [Fact]
    public void should_return_true_for_polygon_when_is_polygon_like_geometry()
    {
        var polygon = _CreateSquare();

        var result = polygon.IsPolygonLikeGeometry();

        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_multipolygon_when_is_polygon_like_geometry()
    {
        var polygon = _CreateSquare();
        var multiPolygon = Factory.CreateMultiPolygon([polygon]);

        var result = multiPolygon.IsPolygonLikeGeometry();

        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_false_for_point_when_is_polygon_like_geometry()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));

        var result = point.IsPolygonLikeGeometry();

        result.Should().BeFalse();
    }

    #endregion

    #region IsSimpleGeometry

    [Fact]
    public void should_return_true_for_point_when_is_simple_geometry()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));

        var result = point.IsSimpleGeometry();

        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_multipoint_when_is_simple_geometry()
    {
        var point1 = Factory.CreatePoint(new Coordinate(0, 0));
        var point2 = Factory.CreatePoint(new Coordinate(1, 1));
        var multiPoint = Factory.CreateMultiPoint([point1, point2]);

        var result = multiPoint.IsSimpleGeometry();

        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_linestring_when_is_simple_geometry()
    {
        var lineString = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);

        var result = lineString.IsSimpleGeometry();

        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_multilinestring_when_is_simple_geometry()
    {
        var line1 = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);
        var line2 = Factory.CreateLineString([new Coordinate(2, 2), new Coordinate(3, 3)]);
        var multiLineString = Factory.CreateMultiLineString([line1, line2]);

        var result = multiLineString.IsSimpleGeometry();

        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_false_for_polygon_when_is_simple_geometry()
    {
        var polygon = _CreateSquare();

        var result = polygon.IsSimpleGeometry();

        result.Should().BeFalse();
    }

    #endregion
}
