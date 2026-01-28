// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;

namespace Headless.Primitives;

[PublicAPI]
[ComplexType]
public sealed class GeoCoordinate : IEquatable<GeoCoordinate>
{
    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public bool Equals(GeoCoordinate? other)
    {
        if (other is null)
        {
            return false;
        }

        return ReferenceEquals(this, other) || (Latitude.Equals(other.Latitude) && Longitude.Equals(other.Longitude));
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || (obj is GeoCoordinate other && Equals(other));
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Latitude, Longitude);
    }

    public override string ToString()
    {
        FormattableString format = $"(lat={Latitude}, long={Longitude})";

        return format.ToString(CultureInfo.InvariantCulture);
    }

    public static bool operator ==(GeoCoordinate? left, GeoCoordinate? right)
    {
        return left?.Equals(right) ?? right is null;
    }

    public static bool operator !=(GeoCoordinate? left, GeoCoordinate? right)
    {
        return !(left == right);
    }
}
