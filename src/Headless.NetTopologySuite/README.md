# Headless.NetTopologySuite

NetTopologySuite extensions for geospatial operations and SQL Server geography compatibility.

## Problem Solved

Provides robust geometry manipulation utilities, precision handling, and SQL Server geography sanitization, solving common issues with geometry validity, coordinate orientation, and precision when working with geospatial data.

## Key Features

- Geometry precision reduction and management
- Permissive geometry operations (intersection, union, difference)
- SQL Server geography sanitization (ring orientation, validation)
- Polygon simplification with topology preservation
- Geometry creation helpers (points, polygons, multi-polygons)
- Coordinate range validation
- Feature collection conversion

## Installation

```bash
dotnet add package Headless.NetTopologySuite
```

## Quick Start

```csharp
using NetTopologySuite.Geometries;

var factory = new GeometryFactory(GeoConstants.HighPrecision, GeoConstants.GoogleMapsSrid);

// Create a polygon from coordinates
var polygon = factory.CreatePolygon(new[]
{
    new Coordinate(0, 0),
    new Coordinate(10, 0),
    new Coordinate(10, 10),
    new Coordinate(0, 10),
    new Coordinate(0, 0)
});

// Sanitize for SQL Server geography
var sanitized = polygon.SanitizeForSqlGeography();

// Simplify polygon
var simplified = polygon.Simplify(GeoConstants.Around1MDegrees);
```

## Usage

### Permissive Operations

```csharp
var intersection = geom1.PermissiveIntersection(geom2);
var union = geom1.PermissiveUnion(geom2);
var overlap = geom1.ComputeOverlap(geom2);
```

### Ring Orientation

```csharp
var fixed = polygon.EnsureIsOrientedCounterClockwise();
```

## Configuration

No configuration required.

## Dependencies

- `NetTopologySuite`
- `NetTopologySuite.Features`
- `Headless.Checks`

## Side Effects

None.
