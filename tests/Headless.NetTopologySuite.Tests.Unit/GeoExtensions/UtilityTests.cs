// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite.Constants;
using NetTopologySuite.Geometries;

namespace Tests.GeoExtensions;

public sealed class UtilityTests
{
    private static GeometryFactory Factory => GeoConstants.GeometryFactory;

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

    #region ContainEmpties

    [Fact]
    public void ContainEmpties_should_return_true_for_empty_geometry()
    {
        var empty = Factory.CreatePolygon();

        var result = empty.ContainEmpties();

        result.Should().BeTrue();
    }

    [Fact]
    public void ContainEmpties_should_return_true_for_linestring()
    {
        var lineString = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);

        var result = lineString.ContainEmpties();

        result.Should().BeTrue();
    }

    [Fact]
    public void ContainEmpties_should_return_true_for_point()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));

        var result = point.ContainEmpties();

        result.Should().BeTrue();
    }

    [Fact]
    public void ContainEmpties_should_return_true_for_collection_with_empty()
    {
        var emptyPolygon = Factory.CreatePolygon();
        var collection = Factory.CreateGeometryCollection([emptyPolygon]);

        var result = collection.ContainEmpties();

        result.Should().BeTrue();
    }

    [Fact]
    public void ContainEmpties_should_return_true_for_collection_with_point()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));
        var collection = Factory.CreateGeometryCollection([point]);

        var result = collection.ContainEmpties();

        result.Should().BeTrue();
    }

    [Fact]
    public void ContainEmpties_should_return_true_for_collection_with_linestring()
    {
        var lineString = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);
        var collection = Factory.CreateGeometryCollection([lineString]);

        var result = collection.ContainEmpties();

        result.Should().BeTrue();
    }

    [Fact]
    public void ContainEmpties_should_return_false_for_valid_polygon()
    {
        var polygon = _CreateSquare();

        var result = polygon.ContainEmpties();

        result.Should().BeFalse();
    }

    #endregion

    #region CreateExpandBy

    [Fact]
    public void CreateExpandBy_should_expand_envelope()
    {
        var envelope = new Envelope(0, 10, 0, 10);

        var result = envelope.CreateExpandBy(5);

        result.MinX.Should().Be(-5);
        result.MaxX.Should().Be(15);
        result.MinY.Should().Be(-5);
        result.MaxY.Should().Be(15);
    }

    [Fact]
    public void CreateExpandBy_should_expand_all_directions()
    {
        var envelope = new Envelope(5, 10, 5, 10);
        var factor = 2.0;

        var result = envelope.CreateExpandBy(factor);

        result.MinX.Should().Be(envelope.MinX - factor);
        result.MaxX.Should().Be(envelope.MaxX + factor);
        result.MinY.Should().Be(envelope.MinY - factor);
        result.MaxY.Should().Be(envelope.MaxY + factor);
    }

    #endregion

    #region Flat

    [Fact]
    public void Flat_should_flatten_geometry_collection()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));
        var lineString = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);
        var innerCollection = Factory.CreateGeometryCollection([point]);
        var outerCollection = Factory.CreateGeometryCollection([innerCollection, lineString]);

        var result = outerCollection.Flat();

        result.Should().HaveCount(2);
        result.Should().Contain(point);
        result.Should().Contain(lineString);
    }

    [Fact]
    public void Flat_should_return_single_geometry_as_array()
    {
        var polygon = _CreateSquare();

        var result = polygon.Flat();

        result.Should().HaveCount(1);
        result[0].Should().Be(polygon);
    }

    #endregion

    #region GetPolygonsOrEmpty

    [Fact]
    public void GetPolygonsOrEmpty_should_return_polygon()
    {
        var polygon = _CreateSquare();

        var result = polygon.GetPolygonsOrEmpty();

        result.Should().HaveCount(1);
        result[0].Should().Be(polygon);
    }

    [Fact]
    public void GetPolygonsOrEmpty_should_extract_from_collection()
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
    public void GetPolygonsOrEmpty_should_return_empty_for_point()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));

        var result = point.GetPolygonsOrEmpty();

        result.Should().BeEmpty();
    }

    #endregion

    #region GetSimpleGeometryOrEmpty

    [Fact]
    public void GetSimpleGeometryOrEmpty_should_return_point()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));

        var result = point.GetSimpleGeometryOrEmpty();

        result.Should().HaveCount(1);
        result[0].Should().Be(point);
    }

    [Fact]
    public void GetSimpleGeometryOrEmpty_should_return_linestring()
    {
        var lineString = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);

        var result = lineString.GetSimpleGeometryOrEmpty();

        result.Should().HaveCount(1);
        result[0].Should().Be(lineString);
    }

    [Fact]
    public void GetSimpleGeometryOrEmpty_should_extract_from_collection()
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
    public void GetSimpleGeometryOrEmpty_should_return_empty_for_polygon()
    {
        var polygon = _CreateSquare();

        var result = polygon.GetSimpleGeometryOrEmpty();

        result.Should().BeEmpty();
    }

    #endregion

    #region IsPolygonLikeGeometry

    [Fact]
    public void IsPolygonLikeGeometry_should_return_true_for_polygon()
    {
        var polygon = _CreateSquare();

        var result = polygon.IsPolygonLikeGeometry();

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPolygonLikeGeometry_should_return_true_for_multipolygon()
    {
        var polygon = _CreateSquare();
        var multiPolygon = Factory.CreateMultiPolygon([polygon]);

        var result = multiPolygon.IsPolygonLikeGeometry();

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPolygonLikeGeometry_should_return_false_for_point()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));

        var result = point.IsPolygonLikeGeometry();

        result.Should().BeFalse();
    }

    #endregion

    #region IsSimpleGeometry

    [Fact]
    public void IsSimpleGeometry_should_return_true_for_point()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));

        var result = point.IsSimpleGeometry();

        result.Should().BeTrue();
    }

    [Fact]
    public void IsSimpleGeometry_should_return_true_for_multipoint()
    {
        var point1 = Factory.CreatePoint(new Coordinate(0, 0));
        var point2 = Factory.CreatePoint(new Coordinate(1, 1));
        var multiPoint = Factory.CreateMultiPoint([point1, point2]);

        var result = multiPoint.IsSimpleGeometry();

        result.Should().BeTrue();
    }

    [Fact]
    public void IsSimpleGeometry_should_return_true_for_linestring()
    {
        var lineString = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);

        var result = lineString.IsSimpleGeometry();

        result.Should().BeTrue();
    }

    [Fact]
    public void IsSimpleGeometry_should_return_true_for_multilinestring()
    {
        var line1 = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);
        var line2 = Factory.CreateLineString([new Coordinate(2, 2), new Coordinate(3, 3)]);
        var multiLineString = Factory.CreateMultiLineString([line1, line2]);

        var result = multiLineString.IsSimpleGeometry();

        result.Should().BeTrue();
    }

    [Fact]
    public void IsSimpleGeometry_should_return_false_for_polygon()
    {
        var polygon = _CreateSquare();

        var result = polygon.IsSimpleGeometry();

        result.Should().BeFalse();
    }

    #endregion
}
