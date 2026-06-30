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

/// <summary>
/// Geospatial extension methods for NetTopologySuite geometries: precision reduction, permissive overlay
/// operations, ring orientation, validity repair, simplification, geometry construction, and SQL Server
/// <c>geography</c> sanitization. Declared in the <c>NetTopologySuite.Geometries</c> namespace so they are
/// available wherever NTS geometries are in scope.
/// </summary>
[PublicAPI]
public static class GeoExtensions
{
    /// <summary>Creates a <see cref="Point"/> at (<paramref name="x"/>, <paramref name="y"/>) using <paramref name="factory"/>.</summary>
    /// <param name="factory">The factory whose SRID and precision model the point inherits.</param>
    /// <param name="x">The X ordinate (longitude for SRID 4326).</param>
    /// <param name="y">The Y ordinate (latitude for SRID 4326).</param>
    /// <returns>A new <see cref="Point"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    [Pure]
    public static Point CreatePoint(this GeometryFactory factory, double x, double y)
    {
        Argument.IsNotNull(factory);

        return factory.CreatePoint(new Coordinate(x, y));
    }

    /// <summary>Projects each point in <paramref name="points"/> to its <see cref="Geometry.Coordinate"/>.</summary>
    /// <param name="points">The points to read coordinates from.</param>
    /// <returns>
    /// An array holding each point's coordinate, in order. An empty point contributes a <see langword="null"/> entry,
    /// because <see cref="Geometry.Coordinate"/> is <see langword="null"/> for empty geometries.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <see langword="null"/>.</exception>
    [Pure]
    public static Coordinate[] ToCoordinates(this IEnumerable<Point> points)
    {
        Argument.IsNotNull(points);

        return [.. points.Select(p => p.Coordinate)];
    }

    /// <summary>
    /// Reduces <paramref name="geometry"/> to <paramref name="geometryFactory"/>'s precision model while preserving
    /// the requested geometry type <typeparamref name="T"/>. Returns the input unchanged when it already carries
    /// that precision model.
    /// </summary>
    /// <typeparam name="T">The geometry type to preserve.</typeparam>
    /// <param name="geometryFactory">Supplies the target precision model.</param>
    /// <param name="geometry">The geometry to reduce.</param>
    /// <returns>The precision-reduced geometry as <typeparamref name="T"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometryFactory"/> or <paramref name="geometry"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The underlying precision reduction fails for an invalid input geometry.</exception>
    /// <exception cref="InvalidOperationException">
    /// Precision reduction changed the geometry to a type other than <typeparamref name="T"/> that could not be
    /// re-wrapped (a single reduced <see cref="Polygon"/> is automatically re-wrapped when <typeparamref name="T"/>
    /// is <see cref="MultiPolygon"/>).
    /// </exception>
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

    /// <summary>
    /// Reduces <paramref name="geometry"/> to <paramref name="precision"/> using topology-preserving snap-rounding
    /// (<see cref="GeometryPrecisionReducer"/>). Returns the input unchanged when it already carries
    /// <paramref name="precision"/>. The resulting geometry type may differ from the input.
    /// </summary>
    /// <param name="geometry">The geometry to reduce.</param>
    /// <param name="precision">The target precision model.</param>
    /// <returns>The precision-reduced geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> or <paramref name="precision"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The reduction fails for an invalid input geometry.</exception>
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

    /// <summary>
    /// Returns the intersection of <paramref name="polygon1"/> and <paramref name="polygon2"/> when they overlap,
    /// or <see langword="null"/> when they do not. Uses the permissive (precision-retry) overlap and intersection helpers.
    /// </summary>
    /// <param name="polygon1">The first geometry.</param>
    /// <param name="polygon2">The second geometry.</param>
    /// <returns>The overlapping region, or <see langword="null"/> when the geometries do not overlap.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygon1"/> or <paramref name="polygon2"/> is <see langword="null"/>.</exception>
    /// <exception cref="TopologyException">The overlap test or the intersection fails even after the reduced-precision retry.</exception>
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

    /// <summary>
    /// Tests whether <paramref name="geometry1"/> and <paramref name="geometry2"/> overlap. Single-element
    /// <see cref="GeometryCollection"/> inputs are unwrapped first. On a <see cref="TopologyException"/> both inputs
    /// are reduced to <see cref="GeoConstants.StreetLevelPrecision"/> and the test is retried once.
    /// </summary>
    /// <param name="geometry1">The first geometry.</param>
    /// <param name="geometry2">The second geometry.</param>
    /// <returns><see langword="true"/> when the geometries overlap; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry1"/> or <paramref name="geometry2"/> is <see langword="null"/>.</exception>
    /// <exception cref="TopologyException">The overlap test fails again after the reduced-precision retry.</exception>
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

    /// <summary>
    /// Computes the intersection of <paramref name="geometry1"/> and <paramref name="geometry2"/>. Single-element
    /// <see cref="GeometryCollection"/> inputs are unwrapped first. On a <see cref="TopologyException"/> both inputs
    /// are reduced to <see cref="GeoConstants.StreetLevelPrecision"/> and the operation is retried once.
    /// </summary>
    /// <param name="geometry1">The first geometry.</param>
    /// <param name="geometry2">The second geometry.</param>
    /// <returns>The intersection geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry1"/> or <paramref name="geometry2"/> is <see langword="null"/>.</exception>
    /// <exception cref="TopologyException">The intersection fails again after the reduced-precision retry.</exception>
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

    /// <summary>
    /// Computes the union of <paramref name="geometry1"/> and <paramref name="geometry2"/>. Single-element
    /// <see cref="GeometryCollection"/> inputs are unwrapped first. On a <see cref="TopologyException"/> both inputs
    /// are reduced to <see cref="GeoConstants.StreetLevelPrecision"/> and the operation is retried once.
    /// </summary>
    /// <param name="geometry1">The first geometry.</param>
    /// <param name="geometry2">The second geometry.</param>
    /// <returns>The union geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry1"/> or <paramref name="geometry2"/> is <see langword="null"/>.</exception>
    /// <exception cref="TopologyException">The union fails again after the reduced-precision retry.</exception>
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

    /// <summary>
    /// Computes the difference of <paramref name="geometry1"/> minus <paramref name="geometry2"/>. Single-element
    /// <see cref="GeometryCollection"/> inputs are unwrapped first. On a <see cref="TopologyException"/> both inputs
    /// are reduced to <see cref="GeoConstants.StreetLevelPrecision"/> and the operation is retried once.
    /// </summary>
    /// <param name="geometry1">The minuend geometry.</param>
    /// <param name="geometry2">The subtrahend geometry.</param>
    /// <returns>The difference geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry1"/> or <paramref name="geometry2"/> is <see langword="null"/>.</exception>
    /// <exception cref="TopologyException">The difference fails again after the reduced-precision retry.</exception>
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

    /// <summary>
    /// Prepares <paramref name="geometry"/> for storage as a SQL Server <c>geography</c> value: validates the SRID
    /// and coordinate ranges, snaps coordinates to <see cref="GeoConstants.SqlServerGeographyPrecision"/>, orients
    /// rings the way SQL Server expects (exterior counter-clockwise, holes clockwise), and repairs any invalidity.
    /// </summary>
    /// <param name="geometry">
    /// The geometry to sanitize; must be non-empty, use SRID <see cref="GeoConstants.GoogleMapsSrid"/>, and contain
    /// only finite coordinates within the WGS84 bounds.
    /// </param>
    /// <returns>A valid, correctly oriented geometry suitable for a SQL Server <c>geography</c> column.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="geometry"/> is empty; its SRID is not <see cref="GeoConstants.GoogleMapsSrid"/>; it contains a
    /// coordinate outside the WGS84 longitude/latitude bounds or a non-finite (NaN/Infinity) ordinate; or it remains
    /// invalid after the repair attempt.
    /// </exception>
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

    /// <summary>
    /// Returns <paramref name="geometry"/> with polygon rings oriented for SQL Server <c>geography</c> (exterior ring
    /// counter-clockwise, holes clockwise), recursing into <see cref="GeometryCollection"/> members. Non-polygonal
    /// geometries are returned unchanged.
    /// </summary>
    /// <param name="geometry">The geometry to orient.</param>
    /// <returns>The oriented geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Returns <paramref name="polygons"/> with every member polygon oriented for SQL Server <c>geography</c>
    /// (exterior ring counter-clockwise, holes clockwise).
    /// </summary>
    /// <param name="polygons">The multi-polygon to orient.</param>
    /// <returns>The oriented multi-polygon; the same instance when it is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygons"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Returns <paramref name="polygon"/> with its exterior ring forced counter-clockwise and any holes clockwise,
    /// as SQL Server <c>geography</c> requires. Rings that are already correctly oriented are kept as-is.
    /// </summary>
    /// <param name="polygon">The polygon to orient.</param>
    /// <returns>The oriented polygon.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <see langword="null"/>.</exception>
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

    /// <summary>Returns a valid version of <paramref name="geom"/>, repairing it when necessary.</summary>
    /// <param name="geom">The geometry to validate and, if needed, repair.</param>
    /// <returns>The input geometry when it is already valid or empty; otherwise a repaired geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geom"/> is <see langword="null"/>.</exception>
    /// <seealso cref="Fix(Geometry, out bool)"/>
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
    /// <param name="geom">The geometry to validate and, if needed, repair.</param>
    /// <param name="wasRepaired">
    /// On return, <see langword="true"/> when <paramref name="geom"/> was invalid and a repair was applied;
    /// otherwise <see langword="false"/>.
    /// </param>
    /// <returns>The input geometry when it is already valid or empty; otherwise a repaired geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geom"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Determines whether <paramref name="polygon"/> follows SQL Server <c>geography</c> orientation: the exterior
    /// ring is counter-clockwise and every hole is clockwise.
    /// </summary>
    /// <param name="polygon">The polygon to inspect.</param>
    /// <returns><see langword="true"/> when the exterior ring is CCW and all holes are CW; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <see langword="null"/>.</exception>
    [Pure]
    public static bool IsOrientedCounterClockwise(this Polygon polygon)
    {
        Argument.IsNotNull(polygon);

        return Orientation.IsCCW(polygon.Shell.CoordinateSequence) && polygon.Holes.All(h => !h.IsCCW);
    }

    /// <summary>
    /// Simplifies <paramref name="polygon"/> with <see cref="TopologyPreservingSimplifier"/>, repairing the result
    /// when simplification leaves it invalid.
    /// </summary>
    /// <param name="polygon">The geometry to simplify.</param>
    /// <param name="distanceTolerance">
    /// Maximum point-displacement distance in the geometry's units (degrees for SRID 4326). Defaults to
    /// <see cref="GeoConstants.Around1MDegrees"/> (~1 m at the equator).
    /// </param>
    /// <returns>The simplified, and if necessary repaired, geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <see langword="null"/>.</exception>
    [Pure]
    public static Geometry Simplify(this Geometry polygon, double distanceTolerance = GeoConstants.Around1MDegrees)
    {
        Argument.IsNotNull(polygon);

        var simple = TopologyPreservingSimplifier.Simplify(polygon, distanceTolerance);

        return simple.IsValid ? simple : simple.Fix();
    }

    /// <summary>
    /// Simplifies <paramref name="polygon"/>, re-orients it for SQL Server <c>geography</c>, and repairs it when
    /// needed. Empty polygons are returned unchanged.
    /// </summary>
    /// <param name="polygon">The polygon to simplify.</param>
    /// <param name="distanceTolerance">
    /// Maximum point-displacement distance in the geometry's units (degrees for SRID 4326). Defaults to
    /// <see cref="GeoConstants.Around1MDegrees"/> (~1 m at the equator).
    /// </param>
    /// <returns>The simplified polygon.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Simplification or the subsequent repair turned the polygon into a non-polygonal geometry; use
    /// <see cref="Simplify(Geometry, double)"/> instead and inspect the result.
    /// </exception>
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

    /// <summary>Simplifies each member polygon of <paramref name="polygons"/>. Empty inputs are returned unchanged.</summary>
    /// <param name="polygons">The multi-polygon to simplify.</param>
    /// <param name="distanceTolerance">
    /// Maximum point-displacement distance in the geometry's units (degrees for SRID 4326). Defaults to
    /// <see cref="GeoConstants.Around1MDegrees"/> (~1 m at the equator).
    /// </param>
    /// <returns>The simplified multi-polygon.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygons"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Simplifying a member polygon produced a non-polygonal geometry.</exception>
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

    /// <summary>
    /// Creates a polygon whose exterior ring runs through <paramref name="points"/> in order, oriented for SQL Server
    /// <c>geography</c>.
    /// </summary>
    /// <param name="factory">The factory used to build the ring and polygon.</param>
    /// <param name="points">The boundary points; must form a valid closed linear ring.</param>
    /// <returns>The created, counter-clockwise-oriented polygon.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> or <paramref name="points"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="points"/> do not form a valid linear ring (not closed, or too few points).</exception>
    [Pure]
    public static Polygon CreatePolygon(this GeometryFactory factory, IEnumerable<Point> points)
    {
        Argument.IsNotNull(factory);
        Argument.IsNotNull(points);

        return CreatePolygon(factory, points.ToCoordinates());
    }

    /// <summary>
    /// Creates a polygon whose exterior ring runs through <paramref name="coordinates"/> in order, oriented for
    /// SQL Server <c>geography</c>.
    /// </summary>
    /// <param name="factory">The factory used to build the ring and polygon.</param>
    /// <param name="coordinates">The boundary coordinates; must form a valid closed linear ring.</param>
    /// <returns>The created, counter-clockwise-oriented polygon.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> or <paramref name="coordinates"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="coordinates"/> do not form a valid linear ring (not closed, or too few points).</exception>
    [Pure]
    public static Polygon CreatePolygon(this GeometryFactory factory, IEnumerable<Coordinate> coordinates)
    {
        Argument.IsNotNull(factory);
        Argument.IsNotNull(coordinates);

        var linearRing = factory.CreateLinearRing(coordinates.AsArray());
        var polygon = factory.CreatePolygon(linearRing);

        return EnsureIsOrientedCounterClockwise(polygon);
    }

    /// <summary>Creates a multi-polygon, building one oriented polygon per coordinate ring in <paramref name="coordinates"/>.</summary>
    /// <param name="factory">The factory used to build the rings and polygons.</param>
    /// <param name="coordinates">One closed linear ring of coordinates per polygon.</param>
    /// <returns>The created multi-polygon.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> or <paramref name="coordinates"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Any element of <paramref name="coordinates"/> does not form a valid linear ring.</exception>
    [Pure]
    public static MultiPolygon CreateMultiPolygon(this GeometryFactory factory, Coordinate[][] coordinates)
    {
        Argument.IsNotNull(factory);
        Argument.IsNotNull(coordinates);

        var polygons = coordinates.ConvertAll(p => CreatePolygon(factory, p));

        return factory.CreateMultiPolygon(polygons);
    }

    /// <summary>Wraps each geometry in a <see cref="Feature"/> (with an empty, ordinal-keyed attributes table) and collects them.</summary>
    /// <param name="geometries">The geometries to wrap.</param>
    /// <returns>A <see cref="FeatureCollection"/> containing one feature per geometry, in order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometries"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Returns <paramref name="geom"/> as a <see cref="MultiPolygon"/>: a multi-polygon passes through and a single
    /// <see cref="Polygon"/> is wrapped, using <paramref name="factory"/>.
    /// </summary>
    /// <param name="factory">The factory used to wrap a single polygon.</param>
    /// <param name="geom">The geometry to convert; must be a <see cref="Polygon"/> or <see cref="MultiPolygon"/>.</param>
    /// <returns>The geometry as a <see cref="MultiPolygon"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> or <paramref name="geom"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="geom"/> is neither a <see cref="Polygon"/> nor a <see cref="MultiPolygon"/>.</exception>
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

    /// <summary>Returns <paramref name="geom"/> as a <see cref="MultiPolygon"/>, using its own factory to wrap a single polygon.</summary>
    /// <param name="geom">The geometry to convert; must be a <see cref="Polygon"/> or <see cref="MultiPolygon"/>.</param>
    /// <returns>The geometry as a <see cref="MultiPolygon"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geom"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="geom"/> is neither a <see cref="Polygon"/> nor a <see cref="MultiPolygon"/>.</exception>
    [Pure]
    public static MultiPolygon AsMultiPolygon(this Geometry geom)
    {
        Argument.IsNotNull(geom);

        return AsMultiPolygon(geom.Factory, geom);
    }

    /// <summary>
    /// Coerces <paramref name="geom"/> to polygonal form: polygons and multi-polygons pass through, while a
    /// <see cref="GeometryCollection"/> is flattened to its contained polygons (returned as a single
    /// <see cref="Polygon"/> or a <see cref="MultiPolygon"/>), recursing into nested sub-collections.
    /// </summary>
    /// <param name="geom">The geometry to coerce.</param>
    /// <returns>A <see cref="Polygon"/> or <see cref="MultiPolygon"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geom"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="geom"/> contains no polygonal geometry to extract.</exception>
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

    /// <summary>
    /// Determines whether <paramref name="overlap"/> is, or contains, empty or lower-dimensional content — an empty
    /// geometry, a bare <see cref="Point"/> or <see cref="LineString"/>, or a <see cref="GeometryCollection"/> with
    /// any empty, point, or line member.
    /// </summary>
    /// <param name="overlap">The geometry to inspect.</param>
    /// <returns><see langword="true"/> when empty or lower-dimensional content is present; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="overlap"/> is <see langword="null"/>.</exception>
    [Pure]
    public static bool ContainsEmpties(this Geometry overlap)
    {
        Argument.IsNotNull(overlap);

        return overlap.IsEmpty
            || overlap is LineString or Point
            || (overlap is GeometryCollection c && c.Geometries.Any(g => g.IsEmpty || g is Point or LineString));
    }

    /// <summary>Returns a new <see cref="Envelope"/> expanded outward by <paramref name="distance"/> on all four sides.</summary>
    /// <param name="envelope">The source envelope.</param>
    /// <param name="distance">The amount to expand on each side, in the envelope's units.</param>
    /// <returns>A new, expanded <see cref="Envelope"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="envelope"/> is <see langword="null"/>.</exception>
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

    /// <summary>Recursively flattens <paramref name="g"/> into its non-collection leaf geometries.</summary>
    /// <param name="g">The geometry to flatten.</param>
    /// <returns>
    /// An array of leaf geometries; a single-element array holding <paramref name="g"/> when it is not a collection.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="g"/> is <see langword="null"/>.</exception>
    [Pure]
    public static Geometry[] Flatten(this Geometry g)
    {
        Argument.IsNotNull(g);

        return g switch
        {
            GeometryCollection c => [.. c.Geometries.SelectMany(x => x.Flatten())],
            _ => [g],
        };
    }

    /// <summary>Recursively extracts every <see cref="Polygon"/> from <paramref name="g"/>, descending into collections.</summary>
    /// <param name="g">The geometry to search.</param>
    /// <returns>An array of the contained polygons; empty when none are found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="g"/> is <see langword="null"/>.</exception>
    [Pure]
    public static Geometry[] GetPolygonsOrEmpty(this Geometry g)
    {
        Argument.IsNotNull(g);

        return g switch
        {
            Polygon => [g],
            GeometryCollection c => [.. c.Geometries.SelectMany(x => x.GetPolygonsOrEmpty())],
            _ => [],
        };
    }

    /// <summary>
    /// Recursively extracts every <see cref="Point"/> and <see cref="LineString"/> from <paramref name="g"/>,
    /// descending into collections.
    /// </summary>
    /// <param name="g">The geometry to search.</param>
    /// <returns>An array of the contained points and line strings; empty when none are found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="g"/> is <see langword="null"/>.</exception>
    [Pure]
    public static Geometry[] GetSimpleGeometryOrEmpty(this Geometry g)
    {
        Argument.IsNotNull(g);

        return g switch
        {
            Point or LineString => [g],
            GeometryCollection c => [.. c.Geometries.SelectMany(x => x.GetSimpleGeometryOrEmpty())],
            _ => [],
        };
    }

    /// <summary>Determines whether <paramref name="g"/> is a <see cref="Polygon"/> or <see cref="MultiPolygon"/>.</summary>
    /// <param name="g">The geometry to test.</param>
    /// <returns><see langword="true"/> for polygonal geometries; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="g"/> is <see langword="null"/>.</exception>
    [Pure]
    public static bool IsPolygonLikeGeometry(this Geometry g)
    {
        Argument.IsNotNull(g);

        return g is Polygon or MultiPolygon;
    }

    /// <summary>
    /// Determines whether <paramref name="g"/> is a <see cref="Point"/>, <see cref="MultiPoint"/>,
    /// <see cref="LineString"/>, or <see cref="MultiLineString"/>.
    /// </summary>
    /// <param name="g">The geometry to test.</param>
    /// <returns><see langword="true"/> for point/line geometries; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="g"/> is <see langword="null"/>.</exception>
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
