# Test Case Design: Headless.NetTopologySuite

**Package:** `src/Headless.NetTopologySuite`
**Test Project:** `Framework.NetTopologySuite.Tests.Unit` (to be created)
**Generated:** 2026-01-25

## Package Overview

Framework.NetTopologySuite provides geospatial utilities wrapping NetTopologySuite library:
- `GeoConstants` - Pre-configured precision models, SRID, geometry factories, GeoJSON converters
- `GeoExtensions` - 30+ extension methods for geometry manipulation, validation, SQL Server compatibility

### Dependencies

- `NetTopologySuite` - Core geometry library
- `NetTopologySuite.IO.GeoJSON4STJ` - System.Text.Json GeoJSON support
- `Framework.Base` - Base framework (Checks, etc.)

---

## Package Analysis

| Category | Files | Public Types | Testable |
|----------|-------|--------------|----------|
| Constants | 1 | GeoConstants | Yes - factory creation, converter config |
| Extensions | 1 | GeoExtensions | Yes - 30+ methods |
| **Total** | **2** | **2** | **All** |

---

## Proposed Test Cases

### 1. GeoConstants Tests

**File:** `tests/Headless.NetTopologySuite.Tests.Unit/GeoConstantsTests.cs`

| Test Case | Description |
|-----------|-------------|
| `GoogleMapsSrid_should_be_4326` | Standard WGS84 SRID |
| `UltraPrecision_should_be_floating` | ~1.1mm accuracy |
| `HighPrecision_should_be_floating_single` | ~11cm accuracy |
| `StreetLevelPrecision_should_be_100000` | ~1.1m accuracy |
| `Around11CmDegrees_should_be_correct_value` | 0.000001 |
| `Around1MDegrees_should_be_correct_value` | 0.00001 |
| `Around111MDegrees_should_be_correct_value` | 0.0001 |
| `NtsGeometryServices_should_be_configured_with_correct_srid` | SRID=4326 |
| `NtsGeometryServices_should_use_high_precision` | HighPrecision model |
| `GeometryFactory_should_have_correct_srid` | SRID=4326 |
| `CreateNtsGeometryServices_should_return_new_instance` | Not singleton |
| `CreateGeoJsonConverter_should_return_converter_with_correct_settings` | RingOrientationOption.EnforceRfc9746 |
| `CreateGeoJsonConverter_should_not_write_bbox` | writeGeometryBBox=false |

---

### 2. GeoExtensions - Point Creation Tests

**File:** `tests/Headless.NetTopologySuite.Tests.Unit/GeoExtensions/PointCreationTests.cs`

| Test Case | Input | Expected |
|-----------|-------|----------|
| `CreatePoint_should_create_point_with_coordinates` | (30.0, 31.0) | Point at (30, 31) |
| `CreatePoint_should_preserve_factory_srid` | Factory with SRID 4326 | Point.SRID=4326 |
| `ToCoordinates_should_convert_points_to_coordinate_array` | [Point1, Point2] | Coordinate[] |
| `ToCoordinates_should_return_empty_for_empty_enumerable` | [] | [] |

---

### 3. GeoExtensions - Precision Tests

**File:** `tests/Headless.NetTopologySuite.Tests.Unit/GeoExtensions/PrecisionTests.cs`

| Test Case | Description |
|-----------|-------------|
| `ChangePrecision_geometry_should_reduce_precision` | High to street level |
| `ChangePrecision_geometry_should_return_same_when_precision_matches` | No change needed |
| `ChangePrecision_factory_should_reduce_precision` | Factory-based reduction |
| `ChangePrecision_factory_should_return_same_when_precision_matches` | No change needed |
| `ChangePrecision_factory_should_convert_multipolygon_to_polygon_and_back` | Type preservation for MultiPolygon demoted to Polygon |

---

### 4. GeoExtensions - Permissive Operations Tests

**File:** `tests/Headless.NetTopologySuite.Tests.Unit/GeoExtensions/PermissiveOperationsTests.cs`

| Test Case | Description |
|-----------|-------------|
| `PermissiveOverlaps_should_return_true_for_overlapping_polygons` | Standard overlap |
| `PermissiveOverlaps_should_return_false_for_non_overlapping` | No overlap |
| `PermissiveOverlaps_should_unwrap_single_geometry_collection` | GeometryCollection{1} unwrapped |
| `PermissiveOverlaps_should_reduce_precision_on_error` | Fallback to StreetLevel |
| `PermissiveIntersection_should_return_intersection` | Standard intersection |
| `PermissiveIntersection_should_unwrap_single_geometry_collection` | GeometryCollection{1} unwrapped |
| `PermissiveIntersection_should_reduce_precision_on_error` | Fallback to StreetLevel |
| `PermissiveUnion_should_return_union` | Standard union |
| `PermissiveUnion_should_unwrap_single_geometry_collection` | GeometryCollection{1} unwrapped |
| `PermissiveUnion_should_reduce_precision_on_error` | Fallback to StreetLevel |
| `PermissiveDifference_should_return_difference` | Standard difference |
| `PermissiveDifference_should_unwrap_single_geometry_collection` | GeometryCollection{1} unwrapped |
| `PermissiveDifference_should_reduce_precision_on_error` | Fallback to StreetLevel |
| `ComputeOverlap_should_return_intersection_when_overlapping` | Non-null result |
| `ComputeOverlap_should_return_null_when_not_overlapping` | No overlap |

---

### 5. GeoExtensions - SQL Geography Sanitization Tests

**File:** `tests/Headless.NetTopologySuite.Tests.Unit/GeoExtensions/SanitizeForSqlGeographyTests.cs`

| Test Case | Input | Expected |
|-----------|-------|----------|
| `should_throw_for_null_geometry` | null | ArgumentNullException |
| `should_throw_for_empty_geometry` | Empty polygon | InvalidOperationException |
| `should_throw_for_wrong_srid` | SRID=0 | InvalidOperationException with message |
| `should_throw_for_invalid_longitude_below_min` | X=-181 | InvalidOperationException |
| `should_throw_for_invalid_longitude_above_max` | X=181 | InvalidOperationException |
| `should_throw_for_invalid_latitude_below_min` | Y=-91 | InvalidOperationException |
| `should_throw_for_invalid_latitude_above_max` | Y=91 | InvalidOperationException |
| `should_reduce_precision` | Ultra precision | High precision output |
| `should_orient_polygon_ccw` | CW polygon | CCW polygon |
| `should_fix_invalid_geometry` | Self-intersecting | Valid geometry |
| `should_throw_for_unfixable_geometry` | Unfixable | InvalidOperationException |
| `should_accept_valid_polygon` | Valid polygon SRID=4326 | Same polygon returned |
| `should_accept_boundary_coordinates` | (-180, -90), (180, 90) | Valid |

---

### 6. GeoExtensions - Orientation Tests

**File:** `tests/Headless.NetTopologySuite.Tests.Unit/GeoExtensions/OrientationTests.cs`

| Test Case | Description |
|-----------|-------------|
| `EnsureIsOrientedCounterClockwise_polygon_should_reverse_cw_shell` | CW to CCW |
| `EnsureIsOrientedCounterClockwise_polygon_should_keep_ccw_shell` | Already CCW |
| `EnsureIsOrientedCounterClockwise_polygon_should_reverse_ccw_holes` | Holes to CW |
| `EnsureIsOrientedCounterClockwise_polygon_should_keep_cw_holes` | Holes already CW |
| `EnsureIsOrientedCounterClockwise_multipolygon_should_orient_all` | All polygons oriented |
| `EnsureIsOrientedCounterClockwise_multipolygon_should_return_empty_unchanged` | Empty multipolygon |
| `EnsureIsOrientedCounterClockwise_geometry_should_handle_polygon` | Dispatch to Polygon |
| `EnsureIsOrientedCounterClockwise_geometry_should_handle_multipolygon` | Dispatch to MultiPolygon |
| `EnsureIsOrientedCounterClockwise_geometry_should_handle_collection` | Recursive handling |
| `EnsureIsOrientedCounterClockwise_geometry_should_return_point_unchanged` | Points pass through |
| `IsOrientedCounterClockwise_should_return_true_for_ccw_shell_cw_holes` | Correct orientation |
| `IsOrientedCounterClockwise_should_return_false_for_cw_shell` | Wrong shell orientation |
| `IsOrientedCounterClockwise_should_return_false_for_ccw_hole` | Wrong hole orientation |

---

### 7. GeoExtensions - Fix Tests

**File:** `tests/Headless.NetTopologySuite.Tests.Unit/GeoExtensions/FixTests.cs`

| Test Case | Input | Expected |
|-----------|-------|----------|
| `Fix_should_return_valid_geometry_unchanged` | Valid polygon | Same instance |
| `Fix_should_return_empty_geometry_unchanged` | Empty polygon | Same instance |
| `Fix_should_fix_self_intersecting_polygon_with_buffer` | Self-intersecting | Valid polygon |
| `Fix_should_use_GeometryFixer_when_buffer_fails` | Complex invalid | Fixed by GeometryFixer |

---

### 8. GeoExtensions - Simplify Tests

**File:** `tests/Headless.NetTopologySuite.Tests.Unit/GeoExtensions/SimplifyTests.cs`

| Test Case | Description |
|-----------|-------------|
| `Simplify_geometry_should_reduce_vertex_count` | Fewer vertices |
| `Simplify_geometry_should_preserve_topology` | Valid after simplify |
| `Simplify_geometry_should_fix_invalid_result` | Auto-fix if needed |
| `Simplify_polygon_should_return_empty_unchanged` | Empty passthrough |
| `Simplify_polygon_should_maintain_ccw_orientation` | Orientation preserved |
| `Simplify_polygon_should_use_default_tolerance` | Around1MDegrees default |
| `Simplify_multipolygon_should_simplify_all_polygons` | Each polygon simplified |
| `Simplify_multipolygon_should_return_empty_unchanged` | Empty passthrough |

---

### 9. GeoExtensions - Polygon Creation Tests

**File:** `tests/Headless.NetTopologySuite.Tests.Unit/GeoExtensions/PolygonCreationTests.cs`

| Test Case | Description |
|-----------|-------------|
| `CreatePolygon_from_points_should_create_valid_polygon` | Points to polygon |
| `CreatePolygon_from_points_should_ensure_ccw` | Oriented correctly |
| `CreatePolygon_from_coordinates_should_create_valid_polygon` | Coordinates to polygon |
| `CreatePolygon_from_coordinates_should_ensure_ccw` | Oriented correctly |
| `CreateMultiPolygon_should_create_from_coordinate_arrays` | 2D array to MultiPolygon |
| `CreateMultiPolygon_should_ensure_all_ccw` | All polygons oriented |

---

### 10. GeoExtensions - Conversion Tests

**File:** `tests/Headless.NetTopologySuite.Tests.Unit/GeoExtensions/ConversionTests.cs`

| Test Case | Description |
|-----------|-------------|
| `ToFeatureCollection_should_create_collection_with_features` | List to FeatureCollection |
| `ToFeatureCollection_should_create_empty_attributes` | Empty AttributesTable |
| `ToFeatureCollection_should_handle_empty_list` | [] to empty collection |
| `AsMultiPolygon_should_wrap_polygon` | Polygon to MultiPolygon |
| `AsMultiPolygon_should_return_multipolygon_unchanged` | MultiPolygon passthrough |
| `AsMultiPolygon_extension_should_use_geometry_factory` | Factory from geometry |
| `EnsurePolygonOrMulti_should_return_polygon` | Polygon passthrough |
| `EnsurePolygonOrMulti_should_return_multipolygon` | MultiPolygon passthrough |
| `EnsurePolygonOrMulti_should_extract_single_polygon_from_collection` | GeometryCollection{Polygon} |
| `EnsurePolygonOrMulti_should_create_multipolygon_from_collection` | GeometryCollection{Polygon, Polygon} |
| `EnsurePolygonOrMulti_should_throw_for_point` | Point | InvalidOperationException |
| `EnsurePolygonOrMulti_should_throw_for_linestring` | LineString | InvalidOperationException |

---

### 11. GeoExtensions - Utility Tests

**File:** `tests/Headless.NetTopologySuite.Tests.Unit/GeoExtensions/UtilityTests.cs`

| Test Case | Description |
|-----------|-------------|
| `ContainEmpties_should_return_true_for_empty_geometry` | Empty polygon |
| `ContainEmpties_should_return_true_for_linestring` | LineString |
| `ContainEmpties_should_return_true_for_point` | Point |
| `ContainEmpties_should_return_true_for_collection_with_empty` | Collection contains empty |
| `ContainEmpties_should_return_false_for_valid_polygon` | Valid polygon |
| `CreateExpandBy_should_expand_envelope` | Expand by factor |
| `CreateExpandBy_should_expand_all_directions` | MinX-factor, MaxX+factor, etc. |
| `Flat_should_flatten_geometry_collection` | Recursive flatten |
| `Flat_should_return_single_geometry_as_array` | Single element array |
| `GetPolygonsOrEmpty_should_return_polygon` | [Polygon] |
| `GetPolygonsOrEmpty_should_extract_from_collection` | Recursive extraction |
| `GetPolygonsOrEmpty_should_return_empty_for_point` | [] |
| `GetSimpleGeometryOrEmpty_should_return_point` | [Point] |
| `GetSimpleGeometryOrEmpty_should_return_linestring` | [LineString] |
| `GetSimpleGeometryOrEmpty_should_extract_from_collection` | Recursive extraction |
| `GetSimpleGeometryOrEmpty_should_return_empty_for_polygon` | [] |
| `IsPolygonLikeGeometry_should_return_true_for_polygon` | true |
| `IsPolygonLikeGeometry_should_return_true_for_multipolygon` | true |
| `IsPolygonLikeGeometry_should_return_false_for_point` | false |
| `IsSimpleGeometry_should_return_true_for_point` | true |
| `IsSimpleGeometry_should_return_true_for_multipoint` | true |
| `IsSimpleGeometry_should_return_true_for_linestring` | true |
| `IsSimpleGeometry_should_return_true_for_multilinestring` | true |
| `IsSimpleGeometry_should_return_false_for_polygon` | false |

---

## Test Summary

| Category | Test Cases |
|----------|------------|
| GeoConstants | 13 |
| Point Creation | 4 |
| Precision | 5 |
| Permissive Operations | 14 |
| SQL Geography Sanitization | 13 |
| Orientation | 13 |
| Fix | 4 |
| Simplify | 8 |
| Polygon Creation | 6 |
| Conversion | 12 |
| Utility | 21 |
| **Total** | **113** |

---

## Test Infrastructure

### Required Test Helpers

```csharp
public static class GeometryTestHelpers
{
    public static GeometryFactory Factory => GeoConstants.GeometryFactory;

    public static Polygon CreateSquare(double size = 1.0, double originX = 0, double originY = 0)
    {
        var coords = new[]
        {
            new Coordinate(originX, originY),
            new Coordinate(originX + size, originY),
            new Coordinate(originX + size, originY + size),
            new Coordinate(originX, originY + size),
            new Coordinate(originX, originY), // Close ring
        };
        return Factory.CreatePolygon(coords);
    }

    public static Polygon CreateClockwiseSquare()
    {
        // CW orientation (shell should be CCW for SQL Server)
        var coords = new[]
        {
            new Coordinate(0, 0),
            new Coordinate(0, 1),
            new Coordinate(1, 1),
            new Coordinate(1, 0),
            new Coordinate(0, 0),
        };
        return Factory.CreatePolygon(coords);
    }

    public static Polygon CreateSelfIntersecting()
    {
        // Figure-8 shape (self-intersecting)
        var coords = new[]
        {
            new Coordinate(0, 0),
            new Coordinate(1, 1),
            new Coordinate(1, 0),
            new Coordinate(0, 1),
            new Coordinate(0, 0),
        };
        return Factory.CreatePolygon(coords);
    }

    public static Polygon CreatePolygonWithWrongSrid()
    {
        var factory = new GeometryFactory(new PrecisionModel(), 0);
        return factory.CreatePolygon([
            new Coordinate(0, 0),
            new Coordinate(1, 0),
            new Coordinate(1, 1),
            new Coordinate(0, 1),
            new Coordinate(0, 0),
        ]);
    }
}
```

### Test Base Class

```csharp
public abstract class GeoTestBase : TestBase
{
    protected GeometryFactory Factory => GeoConstants.GeometryFactory;

    protected Point CreatePoint(double x, double y) => Factory.CreatePoint(new Coordinate(x, y));

    protected Polygon CreateValidPolygon() => GeometryTestHelpers.CreateSquare();
}
```

---

## Priority Order

1. **SQL Geography Sanitization** - Critical for database operations, validates coordinates and SRID
2. **Orientation** - Required for SQL Server geography compatibility
3. **GeoConstants** - Foundation for all geometry operations
4. **Permissive Operations** - Error recovery for geometry operations
5. **Fix/Simplify** - Geometry repair and optimization
6. **Polygon Creation** - Factory methods
7. **Conversion/Utility** - Helper methods

---

## Notes

1. **All methods are testable** - Pure functions operating on geometry objects
2. **No external dependencies** - All tests can be unit tests (no integration tests needed)
3. **Permissive methods have try/catch** - Need to test both success path and fallback path
4. **SQL Server compatibility** - SanitizeForSqlGeography is the most critical method for production use
5. **Coordinate validation** - Latitude [-90, 90], Longitude [-180, 180]
6. **Orientation rules** - Exterior rings CCW, interior rings (holes) CW for SQL Server geography
7. **Precision reduction can change geometry type** - MultiPolygon may become Polygon after precision reduction
8. **Empty geometry handling** - Several methods have special handling for empty geometries
