// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.NetTopologySuite.Constants;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Valid;
using NetTopologySuite.Precision;
using NetTopologySuite.Simplify;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace NetTopologySuite.Geometries;

[PublicAPI]
public static class GeoExtensions
{
    public static Point CreatePoint(this GeometryFactory factory, double x, double y)
    {
        return factory.CreatePoint(new Coordinate(x, y));
    }

    public static Coordinate[] ToCoordinates(this IEnumerable<Point> points)
    {
        return [.. points.Select(p => p.Coordinate)];
    }

    public static T ChangePrecision<T>(this GeometryFactory geometryFactory, T geometry)
        where T : Geometry
    {
        if (geometry.PrecisionModel == geometryFactory.PrecisionModel)
        {
            return geometry;
        }

        var modified = geometry.ChangePrecision(geometryFactory.PrecisionModel);

        if (geometry is MultiPolygon && modified is Polygon polygon)
        {
            return (geometryFactory.CreateMultiPolygon([polygon]) as T)!;
        }

        return (T)modified;
    }

    public static Geometry ChangePrecision(this Geometry geometry, PrecisionModel precision)
    {
        if (geometry.PrecisionModel == precision)
        {
            return geometry;
        }

        return GeometryPrecisionReducer.Reduce(geometry, precision);
    }

    public static Geometry? ComputeOverlap(this Geometry polygon1, Geometry polygon2)
    {
        if (!polygon1.PermissiveOverlaps(polygon2))
        {
            return null;
        }

        return polygon1.PermissiveIntersection(polygon2);
    }

    public static bool PermissiveOverlaps(this Geometry geometry1, Geometry geometry2)
    {
        var geom1 = geometry1 is GeometryCollection { Count: 1 } c1 ? c1.Geometries[0] : geometry1;
        var geom2 = geometry2 is GeometryCollection { Count: 1 } c2 ? c2.Geometries[0] : geometry2;

        try
        {
            return geom1.Overlaps(geom2);
        }
        catch
        {
            geom1 = geom1.ChangePrecision(GeoConstants.StreetLevelPrecision);
            geom2 = geom2.ChangePrecision(GeoConstants.StreetLevelPrecision);

#pragma warning disable ERP022
            return geom1.Overlaps(geom2);
#pragma warning restore ERP022
        }
    }

    public static Geometry PermissiveIntersection(this Geometry geometry1, Geometry geometry2)
    {
        var geom1 = geometry1 is GeometryCollection { Count: 1 } c1 ? c1.Geometries[0] : geometry1;
        var geom2 = geometry2 is GeometryCollection { Count: 1 } c2 ? c2.Geometries[0] : geometry2;

        try
        {
            return geom1.Intersection(geom2);
        }
        catch
        {
            geom1 = geom1.ChangePrecision(GeoConstants.StreetLevelPrecision);
            geom2 = geom2.ChangePrecision(GeoConstants.StreetLevelPrecision);

#pragma warning disable ERP022
            return geom1.Intersection(geom2);
#pragma warning restore ERP022
        }
    }

    public static Geometry PermissiveUnion(this Geometry geometry1, Geometry geometry2)
    {
        var geom1 = geometry1 is GeometryCollection { Count: 1 } c1 ? c1.Geometries[0] : geometry1;
        var geom2 = geometry2 is GeometryCollection { Count: 1 } c2 ? c2.Geometries[0] : geometry2;

        try
        {
            return geom1.Union(geom2);
        }
        catch
        {
            geom1 = geom1.ChangePrecision(GeoConstants.StreetLevelPrecision);
            geom2 = geom2.ChangePrecision(GeoConstants.StreetLevelPrecision);

#pragma warning disable ERP022
            return geom1.Union(geom2);
#pragma warning restore ERP022
        }
    }

    public static Geometry PermissiveDifference(this Geometry geometry1, Geometry geometry2)
    {
        var geom1 = geometry1 is GeometryCollection { Count: 1 } c1 ? c1.Geometries[0] : geometry1;
        var geom2 = geometry2 is GeometryCollection { Count: 1 } c2 ? c2.Geometries[0] : geometry2;

        try
        {
            return geom1.Difference(geom2);
        }
        catch
        {
            geom1 = geom1.ChangePrecision(GeoConstants.StreetLevelPrecision);
            geom2 = geom2.ChangePrecision(GeoConstants.StreetLevelPrecision);

#pragma warning disable ERP022
            return geom1.Difference(geom2);
#pragma warning restore ERP022
        }
    }

    public static Geometry SanitizeForSqlGeography(this Geometry geometry)
    {
        if (geometry == null || geometry.IsEmpty)
        {
            throw new InvalidOperationException("Geometry is null or empty.");
        }

        // 1 Validate geometry is not null, empty, and has correct SRID
        if (geometry.SRID != GeoConstants.GoogleMapsSrid)
        {
            throw new InvalidOperationException(
                $"Geometry SRID must be {GeoConstants.GoogleMapsSrid}, but was {geometry.SRID}."
            );
        }

        // 2 Validate coordinate ranges for geography
        foreach (var coord in geometry.Coordinates)
        {
            if (coord.X < -180 || coord.X > 180 || coord.Y < -90 || coord.Y > 90)
            {
                throw new InvalidOperationException($"Invalid coordinate: ({coord.X}, {coord.Y})");
            }
        }

        // 3 Reduce precision (SQL Server dislikes excessive decimals)
        geometry = geometry.ChangePrecision(GeoConstants.HighPrecision);

        // 4 Fix polygon orientation for SQL Server (outer ring CCW, holes CW)
        geometry = geometry.EnsureIsOrientedCounterClockwise();

        // 5 Validate geometry (SQL Server stricter than NTS)
        var validator = new IsValidOp(geometry);

        if (!validator.IsValid)
        {
            // If geometry is invalid, try to fix it
            geometry = geometry.Fix();

            // Re-validate after fixing
            validator = new IsValidOp(geometry);
        }

        if (!validator.IsValid)
        {
            var reason = validator.ValidationError?.Message ?? "Unknown geometry error";

            throw new InvalidOperationException($"Invalid geometry: {reason}");
        }

        return geometry;
    }

    public static Geometry EnsureIsOrientedCounterClockwise(this Geometry geometry)
    {
        switch (geometry)
        {
            case Polygon polygon:
                return polygon.EnsureIsOrientedCounterClockwise();
            case MultiPolygon multiPolygon:
                return multiPolygon.EnsureIsOrientedCounterClockwise();
            case GeometryCollection collection:
            {
                var geom = new Geometry[collection.NumGeometries];

                for (var i = 0; i < collection.NumGeometries; i++)
                {
                    geom[i] = EnsureIsOrientedCounterClockwise(collection.GetGeometryN(i));
                }

                var result = geometry.Factory.CreateGeometryCollection(geom);

                return result;
            }
            default:
                return geometry;
        }
    }

    public static MultiPolygon EnsureIsOrientedCounterClockwise(this MultiPolygon polygons)
    {
        if (polygons.IsEmpty)
        {
            return polygons;
        }

        var items = polygons.Geometries.OfType<Polygon>().Select(EnsureIsOrientedCounterClockwise).ToArray();
        var multiPolygon = polygons.Factory.CreateMultiPolygon(items);

        return multiPolygon;
    }

    public static Polygon EnsureIsOrientedCounterClockwise(this Polygon polygon)
    {
        // SQL Server's geography type requires:
        // - The exterior ring (shell) must be counter-clockwise
        // - Any interior rings (holes) must be clockwise

        var shell = !Orientation.IsCCW(polygon.Shell.CoordinateSequence) ? _Reverse(polygon.Shell) : polygon.Shell;
        var holes = polygon.Holes.ConvertAll(h => Orientation.IsCCW(h.CoordinateSequence) ? _Reverse(h) : h);

        return polygon.Factory.CreatePolygon(shell, holes);
    }

    public static Geometry Fix(this Geometry geom)
    {
        if (geom.IsValid || geom.IsEmpty)
        {
            return geom;
        }

        if (geom is Polygon or MultiPolygon)
        {
            geom = geom.Buffer(0);
        }

        if (geom.IsValid)
        {
            return geom;
        }

        return GeometryFixer.Fix(geom, isKeepMulti: false);
    }

    public static bool IsOrientedCounterClockwise(this Polygon polygon)
    {
        return Orientation.IsCCW(polygon.Shell.CoordinateSequence) && polygon.Holes.All(h => !h.IsCCW);
    }

    public static Geometry Simplify(this Geometry polygon, double distanceTolerance = GeoConstants.Around1MDegrees)
    {
        var simple = TopologyPreservingSimplifier.Simplify(polygon, distanceTolerance);

        return simple.IsValid ? simple : simple.Fix();
    }

    public static Polygon Simplify(this Polygon polygon, double distanceTolerance = GeoConstants.Around1MDegrees)
    {
        if (polygon.IsEmpty)
        {
            return polygon;
        }

        polygon = (Polygon)((Geometry)polygon).Simplify(distanceTolerance);
        polygon = polygon.EnsureIsOrientedCounterClockwise();

        return polygon.IsValid ? polygon : (Polygon)polygon.Fix();
    }

    public static MultiPolygon Simplify(
        this MultiPolygon polygons,
        double distanceTolerance = GeoConstants.Around1MDegrees
    )
    {
        if (polygons.IsEmpty)
        {
            return polygons;
        }

        var items = polygons.Geometries.ConvertAll(geometry => ((Polygon)geometry).Simplify(distanceTolerance));
        var simplified = polygons.Factory.CreateMultiPolygon(items);

        return simplified;
    }

    public static Polygon CreatePolygon(this GeometryFactory factory, IEnumerable<Point> points)
    {
        return CreatePolygon(factory, points.ToCoordinates());
    }

    public static Polygon CreatePolygon(this GeometryFactory factory, IEnumerable<Coordinate> coordinates)
    {
        var linearRing = factory.CreateLinearRing(coordinates.AsArray());
        var polygon = factory.CreatePolygon(linearRing);

        return EnsureIsOrientedCounterClockwise(polygon);
    }

    public static MultiPolygon CreateMultiPolygon(this GeometryFactory factory, Coordinate[][] coordinates)
    {
        var polygons = coordinates.ConvertAll(p => CreatePolygon(factory, p));

        return factory.CreateMultiPolygon(polygons);
    }

    public static FeatureCollection ToFeatureCollection(this List<Geometry> geometries)
    {
        var collection = new FeatureCollection();

        foreach (var geometry in geometries)
        {
            collection.Add(new Feature(geometry, new AttributesTable(StringComparer.Ordinal)));
        }

        return collection;
    }

    public static MultiPolygon AsMultiPolygon(this GeometryFactory factory, Geometry geom)
    {
        return geom as MultiPolygon ?? factory.CreateMultiPolygon([(Polygon)geom]);
    }

    public static MultiPolygon AsMultiPolygon(this Geometry geom)
    {
        return AsMultiPolygon(geom.Factory, geom);
    }

    public static Geometry EnsurePolygonOrMulti(this Geometry geom)
    {
        if (geom is Polygon or MultiPolygon)
        {
            return geom;
        }

        if (geom is GeometryCollection collection)
        {
            var polygons = collection.Geometries.Where(g => g is Polygon).ToArray();

            switch (polygons.Length)
            {
                case 1:
                    return polygons[0];
                case > 1:
                    return geom.Factory.CreateMultiPolygon(polygons.Cast<Polygon>().ToArray());
            }
        }

        throw new InvalidOperationException(
            $"Geometry must be a Polygon or MultiPolygon, but was {geom.GeometryType}."
        );
    }

    public static bool ContainEmpties(this Geometry overlap)
    {
        return overlap.IsEmpty
            || overlap is LineString or Point
            || (overlap is GeometryCollection c && c.Geometries.Any(g => g.IsEmpty || g is Point or LineString));
    }

    public static Envelope CreateExpandBy(this Envelope envelope, double expandFactor)
    {
        return new Envelope(
            envelope.MinX - expandFactor,
            envelope.MaxX + expandFactor,
            envelope.MinY - expandFactor,
            envelope.MaxY + expandFactor
        );
    }

    public static Geometry[] Flat(this Geometry g)
    {
        return g switch
        {
            GeometryCollection c => c.Geometries.SelectMany(x => x.Flat()).ToArray(),
            _ => [g],
        };
    }

    public static Geometry[] GetPolygonsOrEmpty(this Geometry g)
    {
        return g switch
        {
            Polygon => [g],
            GeometryCollection c => c.Geometries.SelectMany(x => x.GetPolygonsOrEmpty()).ToArray(),
            _ => [],
        };
    }

    public static Geometry[] GetSimpleGeometryOrEmpty(this Geometry g)
    {
        return g switch
        {
            Point or LineString => [g],
            GeometryCollection c => c.Geometries.SelectMany(x => x.GetSimpleGeometryOrEmpty()).ToArray(),
            _ => [],
        };
    }

    public static bool IsPolygonLikeGeometry(this Geometry g)
    {
        return g is Polygon or MultiPolygon;
    }

    public static bool IsSimpleGeometry(this Geometry g)
    {
        return g is Point or MultiPoint or LineString or MultiLineString;
    }

    #region Helpers

    private static T _Reverse<T>(T geometry)
        where T : Geometry => (T)geometry.Reverse();

    #endregion
}
