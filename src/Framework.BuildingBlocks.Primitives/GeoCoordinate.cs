using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Framework.BuildingBlocks.Domains;
using NetTopologySuite.Geometries;

namespace Framework.BuildingBlocks.Primitives;

[ComplexType]
[PublicAPI]
public sealed class GeoCoordinate : ValueObject
{
    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public override string ToString()
    {
        FormattableString format = $"(lat={Latitude}, long={Longitude})";

        return format.ToString(CultureInfo.InvariantCulture);
    }

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }

    public Coordinate ToCoordinate() => this;

    [return: NotNullIfNotNull(nameof(operand))]
    public static GeoCoordinate? FromCoordinate(Coordinate? operand) => operand;

    public static GeoCoordinate? FromPoint(Point? operand) => operand;

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator GeoCoordinate?(Coordinate? operand)
    {
        return operand is null ? null : new() { Latitude = operand.Y, Longitude = operand.X };
    }

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator Coordinate?(GeoCoordinate? operand)
    {
        return operand is null ? null : new(operand.Longitude, operand.Latitude);
    }

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator GeoCoordinate?(Point? operand)
    {
        return operand is null ? null : new() { Latitude = operand.Y, Longitude = operand.X };
    }
}
