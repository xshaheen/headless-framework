// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Implementation;
using NetTopologySuite.IO.Converters;

namespace Headless.NetTopologySuite.Constants;

[PublicAPI]
public static class GeoConstants
{
    public const int GoogleMapsSrid = 4326;

    /// <summary>Minimum valid longitude (X) for WGS84 / SRID 4326.</summary>
    public const double MinLongitude = -180d;

    /// <summary>Maximum valid longitude (X) for WGS84 / SRID 4326.</summary>
    public const double MaxLongitude = 180d;

    /// <summary>Minimum valid latitude (Y) for WGS84 / SRID 4326.</summary>
    public const double MinLatitude = -90d;

    /// <summary>Maximum valid latitude (Y) for WGS84 / SRID 4326.</summary>
    public const double MaxLatitude = 90d;

    /// <summary>
    /// Full IEEE-754 double-precision floating model (Scale = 0, ~15-16 significant digits).
    /// Coordinates are not snapped to a grid; absolute accuracy is value-magnitude dependent.
    /// </summary>
    public static readonly PrecisionModel UltraPrecision = PrecisionModel.Floating.Value;

    /// <summary>
    /// IEEE-754 single-precision floating model (Scale = 0, ~7 significant digits).
    /// Not a fixed grid: absolute accuracy is magnitude dependent (sub-centimetre near the
    /// origin, degrading to roughly metres near ±180° longitude for SRID 4326).
    /// </summary>
    public static readonly PrecisionModel HighPrecision = PrecisionModel.FloatingSingle.Value;

    /// <summary>
    /// Fixed precision with scale 1e5 — coordinates are snapped to a 1e-5 grid
    /// (~1.1 m at the equator when the SRID 4326 units are degrees).
    /// </summary>
    public static readonly PrecisionModel StreetLevelPrecision = new(100_000); // scale 1e5

    /// <summary>~11 cm at Equator, ~9.6 cm at Cairo (~30°N)</summary>
    public const double Around11CmDegrees = 0.000001;

    /// <summary>~1.1 m at Equator, ~0.96 m at Cairo (~30°N)</summary>
    public const double Around1MDegrees = 0.00001;

    /// <summary>~111 m at Equator, ~96 m at Cairo (~30°N)</summary>
    public const double Around111MDegrees = 0.0001;

    public static readonly NtsGeometryServices NtsGeometryServices = CreateNtsGeometryServices();

    public static readonly GeometryFactory GeometryFactory = NtsGeometryServices.CreateGeometryFactory();

    public static NtsGeometryServices CreateNtsGeometryServices()
    {
        return new(
            CoordinateArraySequenceFactory.Instance,
            HighPrecision,
            GoogleMapsSrid,
            GeometryOverlay.NG,
            GeometryRelate.NG,
            new CoordinateEqualityComparer()
        );
    }

    public static GeoJsonConverterFactory CreateGeoJsonConverter()
    {
        return new GeoJsonConverterFactory(
            factory: GeometryFactory,
            writeGeometryBBox: false,
            idPropertyName: "id",
            ringOrientationOption: RingOrientationOption.EnforceRfc9746, // To match expected in SQL Server
            allowModifyingAttributesTables: false
        );
    }
}
