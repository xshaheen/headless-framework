// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.NetTopologySuite.Constants;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Valid;
using NetTopologySuite.Precision;
using NetTopologySuite.Simplify;

// Declared in the NTS namespace so consumers get these extensions without an explicit
// `using Headless.NetTopologySuite` directive. Re-audit method names on each NetTopologySuite
// upgrade for collisions with newly added first-party NTS extension methods.
#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace NetTopologySuite.Geometries;

[PublicAPI]
public static class GeoExtensions
{
    [Pure]
    public static Point CreatePoint(this GeometryFactory factory, double x, double y)
    {
        Argument.IsNotNull(factory);

        return factory.CreatePoint(new Coordinate(x, y));
    }

    [Pure]
    public static Coordinate[] ToCoordinates(this IEnumerable<Point> points)
    {
        Argument.IsNotNull(points);

        return [.. points.Select(p => p.Coordinate)];
    }

    [Pure]
    public static T ChangePrecision<T>(this GeometryFactory geometryFactory, T geometry)
        where T : Geometry
    {
        Argument.IsNotNull(geometryFactory);
        Argument.IsNotNull(geometry);

        if (geometry.PrecisionModel == geometryFactory.PrecisionModel)
        {
            return geometry;
        }

        var modified = geometry.ChangePrecision(geometryFactory.PrecisionModel);

        if (geometry is MultiPolygon && modified is Polygon polygon)
        {
            return (geometryFactory.CreateMultiPolygon([polygon]) as T)!;
        }

        // Precision reduction can change the geometry type in either direction (e.g. a
        // self-intersecting Polygon splits into a MultiPolygon). Surface a descriptive error
        // instead of a raw InvalidCastException from the blind `(T)modified` cast.
        if (modified is not T result)
        {
            throw new InvalidOperationException(
                $"Precision reduction changed geometry type from {typeof(T).Name} to {modified.GeometryType}."
            );
        }

        return result;
    }

    [Pure]
    public static Geometry ChangePrecision(this Geometry geometry, PrecisionModel precision)
    {
        Argument.IsNotNull(geometry);
        Argument.IsNotNull(precision);

        if (geometry.PrecisionModel == precision)
        {
            return geometry;
        }

        return GeometryPrecisionReducer.Reduce(geometry, precision);
    }

    [Pure]
    public static Geometry? ComputeOverlap(this Geometry polygon1, Geometry polygon2)
    {
        Argument.IsNotNull(polygon1);
        Argument.IsNotNull(polygon2);

        if (!polygon1.PermissiveOverlaps(polygon2))
        {
            return null;
        }

        return polygon1.PermissiveIntersection(polygon2);
    }

    [Pure]
    public static bool PermissiveOverlaps(this Geometry geometry1, Geometry geometry2)
    {
        Argument.IsNotNull(geometry1);
        Argument.IsNotNull(geometry2);

        var geom1 = geometry1 is GeometryCollection { Count: 1 } c1 ? c1.Geometries[0] : geometry1;
        var geom2 = geometry2 is GeometryCollection { Count: 1 } c2 ? c2.Geometries[0] : geometry2;

        try
        {
            return geom1.Overlaps(geom2);
        }
        catch (TopologyException)
        {
            geom1 = geom1.ChangePrecision(GeoConstants.StreetLevelPrecision);
            geom2 = geom2.ChangePrecision(GeoConstants.StreetLevelPrecision);

#pragma warning disable ERP022
            return geom1.Overlaps(geom2);
#pragma warning restore ERP022
        }
    }

    [Pure]
    public static Geometry PermissiveIntersection(this Geometry geometry1, Geometry geometry2)
    {
        Argument.IsNotNull(geometry1);
        Argument.IsNotNull(geometry2);

        var geom1 = geometry1 is GeometryCollection { Count: 1 } c1 ? c1.Geometries[0] : geometry1;
        var geom2 = geometry2 is GeometryCollection { Count: 1 } c2 ? c2.Geometries[0] : geometry2;

        try
        {
            return geom1.Intersection(geom2);
        }
        catch (TopologyException)
        {
            geom1 = geom1.ChangePrecision(GeoConstants.StreetLevelPrecision);
            geom2 = geom2.ChangePrecision(GeoConstants.StreetLevelPrecision);

#pragma warning disable ERP022
            return geom1.Intersection(geom2);
#pragma warning restore ERP022
        }
    }

    [Pure]
    public static Geometry PermissiveUnion(this Geometry geometry1, Geometry geometry2)
    {
        Argument.IsNotNull(geometry1);
        Argument.IsNotNull(geometry2);

        var geom1 = geometry1 is GeometryCollection { Count: 1 } c1 ? c1.Geometries[0] : geometry1;
        var geom2 = geometry2 is GeometryCollection { Count: 1 } c2 ? c2.Geometries[0] : geometry2;

        try
        {
            return geom1.Union(geom2);
        }
        catch (TopologyException)
        {
            geom1 = geom1.ChangePrecision(GeoConstants.StreetLevelPrecision);
            geom2 = geom2.ChangePrecision(GeoConstants.StreetLevelPrecision);

#pragma warning disable ERP022
            return geom1.Union(geom2);
#pragma warning restore ERP022
        }
    }

    [Pure]
    public static Geometry PermissiveDifference(this Geometry geometry1, Geometry geometry2)
    {
        Argument.IsNotNull(geometry1);
        Argument.IsNotNull(geometry2);

        var geom1 = geometry1 is GeometryCollection { Count: 1 } c1 ? c1.Geometries[0] : geometry1;
        var geom2 = geometry2 is GeometryCollection { Count: 1 } c2 ? c2.Geometries[0] : geometry2;

        try
        {
            return geom1.Difference(geom2);
        }
        catch (TopologyException)
        {
            geom1 = geom1.ChangePrecision(GeoConstants.StreetLevelPrecision);
            geom2 = geom2.ChangePrecision(GeoConstants.StreetLevelPrecision);

#pragma warning disable ERP022
            return geom1.Difference(geom2);
#pragma warning restore ERP022
        }
    }

    [Pure]
    public static Geometry SanitizeForSqlGeography(this Geometry geometry)
    {
        Argument.IsNotNull(geometry);

        if (geometry.IsEmpty)
        {
            throw new InvalidOperationException("Geometry is empty.");
        }

        // 1 Validate geometry has the correct SRID
        if (geometry.SRID != GeoConstants.GoogleMapsSrid)
        {
            FormattableString format = $"Geometry SRID must be {GeoConstants.GoogleMapsSrid}, but was {geometry.SRID}.";
            throw new InvalidOperationException(format.ToString(CultureInfo.InvariantCulture));
        }

        // 2 Validate coordinate ranges for geography
        foreach (var coord in geometry.Coordinates)
        {
            var isOutOfRange =
                coord.X < GeoConstants.MinLongitude
                || coord.X > GeoConstants.MaxLongitude
                || coord.Y < GeoConstants.MinLatitude
                || coord.Y > GeoConstants.MaxLatitude;

            // NaN fails every ordered comparison above, so non-finite values must be rejected explicitly.
            if (isOutOfRange || !double.IsFinite(coord.X) || !double.IsFinite(coord.Y))
            {
                FormattableString format = $"Invalid coordinate: ({coord.X}, {coord.Y})";
                throw new InvalidOperationException(format.ToString(CultureInfo.InvariantCulture));
            }
        }

        // 3 Reduce coordinate precision to a fixed grid (SQL Server dislikes excessive decimals).
        // Must use a fixed-scale model: reducing to the (floating) HighPrecision model is a no-op
        // because the geometry already carries it and GeometryFactory never snapped the coordinates.
        // Use pointwise reduction (not topology-preserving) so it never runs OverlayNG and cannot
        // throw on an invalid input; any invalidity introduced by snapping is repaired in step 5.
        geometry = GeometryPrecisionReducer.ReducePointwise(geometry, GeoConstants.SqlServerGeographyPrecision);

        // 4 Fix polygon orientation for SQL Server (outer ring CCW, holes CW)
        geometry = geometry.EnsureIsOrientedCounterClockwise();

        // 5 Validate geometry (SQL Server stricter than NTS)
        var validator = new IsValidOp(geometry);

        if (!validator.IsValid)
        {
            // Already known invalid here, so skip Fix()'s redundant IsValid re-check.
            geometry = _FixKnownInvalid(geometry);

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

    [Pure]
    public static Geometry EnsureIsOrientedCounterClockwise(this Geometry geometry)
    {
        Argument.IsNotNull(geometry);

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

    [Pure]
    public static MultiPolygon EnsureIsOrientedCounterClockwise(this MultiPolygon polygons)
    {
        Argument.IsNotNull(polygons);

        if (polygons.IsEmpty)
        {
            return polygons;
        }

        var items = polygons.Geometries.OfType<Polygon>().Select(EnsureIsOrientedCounterClockwise).ToArray();
        var multiPolygon = polygons.Factory.CreateMultiPolygon(items);

        return multiPolygon;
    }

    [Pure]
    public static Polygon EnsureIsOrientedCounterClockwise(this Polygon polygon)
    {
        Argument.IsNotNull(polygon);

        // SQL Server's geography type requires:
        // - The exterior ring (shell) must be counter-clockwise
        // - Any interior rings (holes) must be clockwise

        var shell = !Orientation.IsCCW(polygon.Shell.CoordinateSequence)
            ? (LinearRing)polygon.Shell.Reverse()
            : polygon.Shell;
        var holes = polygon.Holes.ConvertAll(h =>
            Orientation.IsCCW(h.CoordinateSequence) ? (LinearRing)h.Reverse() : h
        );

        return polygon.Factory.CreatePolygon(shell, holes);
    }

    [Pure]
    public static Geometry Fix(this Geometry geom)
    {
        return geom.Fix(out _);
    }

    /// <summary>
    /// Returns a valid version of <paramref name="geom"/>, repairing it when necessary.
    /// <paramref name="wasRepaired"/> is <see langword="true"/> when the input was invalid and a
    /// repair (Buffer(0) / <see cref="GeometryFixer"/>) was applied — a repair may silently alter
    /// or drop parts of the geometry, so callers that care about fidelity should inspect the result.
    /// </summary>
    [Pure]
    public static Geometry Fix(this Geometry geom, out bool wasRepaired)
    {
        Argument.IsNotNull(geom);

        if (geom.IsValid || geom.IsEmpty)
        {
            wasRepaired = false;

            return geom;
        }

        wasRepaired = true;

        return _FixKnownInvalid(geom);
    }

    [Pure]
    public static bool IsOrientedCounterClockwise(this Polygon polygon)
    {
        Argument.IsNotNull(polygon);

        return Orientation.IsCCW(polygon.Shell.CoordinateSequence) && polygon.Holes.All(h => !h.IsCCW);
    }

    [Pure]
    public static Geometry Simplify(this Geometry polygon, double distanceTolerance = GeoConstants.Around1MDegrees)
    {
        Argument.IsNotNull(polygon);

        var simple = TopologyPreservingSimplifier.Simplify(polygon, distanceTolerance);

        return simple.IsValid ? simple : simple.Fix();
    }

    [Pure]
    public static Polygon Simplify(this Polygon polygon, double distanceTolerance = GeoConstants.Around1MDegrees)
    {
        Argument.IsNotNull(polygon);

        if (polygon.IsEmpty)
        {
            return polygon;
        }

        // Simplification or the subsequent Fix() (Buffer(0) / GeometryFixer) can turn a polygon
        // into a MultiPolygon. Surface a descriptive error instead of a raw InvalidCastException;
        // callers that may hit this case should use Simplify(Geometry) and inspect the result.
        if (((Geometry)polygon).Simplify(distanceTolerance) is not Polygon simplified)
        {
            throw new InvalidOperationException(
                "Simplification changed the polygon into a non-polygonal geometry; use Simplify(Geometry) instead."
            );
        }

        simplified = simplified.EnsureIsOrientedCounterClockwise();

        if (simplified.IsValid)
        {
            return simplified;
        }

        if (simplified.Fix() is not Polygon fixedPolygon)
        {
            throw new InvalidOperationException(
                "Fixing the simplified polygon produced a non-polygonal geometry; use Simplify(Geometry) instead."
            );
        }

        return fixedPolygon;
    }

    [Pure]
    public static MultiPolygon Simplify(
        this MultiPolygon polygons,
        double distanceTolerance = GeoConstants.Around1MDegrees
    )
    {
        Argument.IsNotNull(polygons);

        if (polygons.IsEmpty)
        {
            return polygons;
        }

        var items = polygons.Geometries.ConvertAll(geometry => ((Polygon)geometry).Simplify(distanceTolerance));
        var simplified = polygons.Factory.CreateMultiPolygon(items);

        return simplified;
    }

    [Pure]
    public static Polygon CreatePolygon(this GeometryFactory factory, IEnumerable<Point> points)
    {
        Argument.IsNotNull(factory);
        Argument.IsNotNull(points);

        return CreatePolygon(factory, points.ToCoordinates());
    }

    [Pure]
    public static Polygon CreatePolygon(this GeometryFactory factory, IEnumerable<Coordinate> coordinates)
    {
        Argument.IsNotNull(factory);
        Argument.IsNotNull(coordinates);

        var linearRing = factory.CreateLinearRing(coordinates.AsArray());
        var polygon = factory.CreatePolygon(linearRing);

        return EnsureIsOrientedCounterClockwise(polygon);
    }

    [Pure]
    public static MultiPolygon CreateMultiPolygon(this GeometryFactory factory, Coordinate[][] coordinates)
    {
        Argument.IsNotNull(factory);
        Argument.IsNotNull(coordinates);

        var polygons = coordinates.ConvertAll(p => CreatePolygon(factory, p));

        return factory.CreateMultiPolygon(polygons);
    }

    [Pure]
    public static FeatureCollection ToFeatureCollection(this IEnumerable<Geometry> geometries)
    {
        Argument.IsNotNull(geometries);

        var collection = new FeatureCollection();

        foreach (var geometry in geometries)
        {
            collection.Add(new Feature(geometry, new AttributesTable(StringComparer.Ordinal)));
        }

        return collection;
    }

    [Pure]
    public static MultiPolygon AsMultiPolygon(this GeometryFactory factory, Geometry geom)
    {
        Argument.IsNotNull(factory);
        Argument.IsNotNull(geom);

        return geom switch
        {
            MultiPolygon multiPolygon => multiPolygon,
            Polygon polygon => factory.CreateMultiPolygon([polygon]),
            _ => throw new InvalidOperationException(
                $"Geometry must be a Polygon or MultiPolygon, but was {geom.GeometryType}."
            ),
        };
    }

    [Pure]
    public static MultiPolygon AsMultiPolygon(this Geometry geom)
    {
        Argument.IsNotNull(geom);

        return AsMultiPolygon(geom.Factory, geom);
    }

    [Pure]
    public static Geometry EnsurePolygonOrMulti(this Geometry geom)
    {
        Argument.IsNotNull(geom);

        if (geom is Polygon or MultiPolygon)
        {
            return geom;
        }

        if (geom is GeometryCollection)
        {
            // Recurse into nested sub-collections so polygons are not silently dropped.
            var polygons = geom.GetPolygonsOrEmpty().Cast<Polygon>().ToArray();

            switch (polygons.Length)
            {
                case 1:
                    return polygons[0];
                case > 1:
                    return geom.Factory.CreateMultiPolygon(polygons);
            }
        }

        throw new InvalidOperationException(
            $"Geometry must be a Polygon or MultiPolygon, but was {geom.GeometryType}."
        );
    }

    [Pure]
    public static bool ContainsEmpties(this Geometry overlap)
    {
        Argument.IsNotNull(overlap);

        return overlap.IsEmpty
            || overlap is LineString or Point
            || (overlap is GeometryCollection c && c.Geometries.Any(g => g.IsEmpty || g is Point or LineString));
    }

    [Pure]
    public static Envelope CreateExpandBy(this Envelope envelope, double distance)
    {
        Argument.IsNotNull(envelope);

        return new Envelope(
            envelope.MinX - distance,
            envelope.MaxX + distance,
            envelope.MinY - distance,
            envelope.MaxY + distance
        );
    }

    [Pure]
    public static Geometry[] Flatten(this Geometry g)
    {
        Argument.IsNotNull(g);

        return g switch
        {
            GeometryCollection c => c.Geometries.SelectMany(x => x.Flatten()).ToArray(),
            _ => [g],
        };
    }

    [Pure]
    public static Geometry[] GetPolygonsOrEmpty(this Geometry g)
    {
        Argument.IsNotNull(g);

        return g switch
        {
            Polygon => [g],
            GeometryCollection c => c.Geometries.SelectMany(x => x.GetPolygonsOrEmpty()).ToArray(),
            _ => [],
        };
    }

    [Pure]
    public static Geometry[] GetSimpleGeometryOrEmpty(this Geometry g)
    {
        Argument.IsNotNull(g);

        return g switch
        {
            Point or LineString => [g],
            GeometryCollection c => c.Geometries.SelectMany(x => x.GetSimpleGeometryOrEmpty()).ToArray(),
            _ => [],
        };
    }

    [Pure]
    public static bool IsPolygonLikeGeometry(this Geometry g)
    {
        Argument.IsNotNull(g);

        return g is Polygon or MultiPolygon;
    }

    [Pure]
    public static bool IsSimpleGeometry(this Geometry g)
    {
        Argument.IsNotNull(g);

        return g is Point or MultiPoint or LineString or MultiLineString;
    }

    #region Helpers

    // Precondition: geom is already known to be invalid and non-empty.
    private static Geometry _FixKnownInvalid(Geometry geom)
    {
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

    #endregion
}
