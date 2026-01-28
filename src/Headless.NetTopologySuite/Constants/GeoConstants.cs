// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Implementation;
using NetTopologySuite.IO.Converters;

namespace Headless.NetTopologySuite.Constants;

public static class GeoConstants
{
    public const int GoogleMapsSrid = 4326;

    /// <summary>Accuracy: ~1.1 mm, Ultra precision</summary>
    public static readonly PrecisionModel UltraPrecision = PrecisionModel.Floating.Value; // 1e12

    /// <summary>Accuracy: ~11 cm, High precision (GPS, cadastral)</summary>
    public static readonly PrecisionModel HighPrecision = PrecisionModel.FloatingSingle.Value; // 1e6

    /// <summary>Accuracy: ~1.1 m, Street-level (good)</summary>
    public static readonly PrecisionModel StreetLevelPrecision = new(100_000); // 1e5

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
