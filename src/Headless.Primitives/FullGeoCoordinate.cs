// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// Represents a geographical location that is determined by latitude and longitude
/// coordinates. May also include altitude, accuracy, speed, and course information.
/// <para>
/// Note: There is currently no support GeoCoordinate in .NET. The following class was copied from:
/// https://github.com/ghuntley/geocoordinate/blob/master/src/GeoCoordinatePortable/GeoCoordinate.cs
/// </para>
/// </summary>
[PublicAPI]
public sealed class FullGeoCoordinate : IEquatable<FullGeoCoordinate>
{
    /// <summary>
    ///     Represents a <see cref="FullGeoCoordinate"/> object that has unknown latitude and longitude fields.
    /// </summary>
    public static readonly FullGeoCoordinate Unknown = new();

    /// <summary>
    ///     Initializes a new instance of the GeoCoordinate class from latitude and longitude. Set
    ///     <see cref="Altitude"/>, <see cref="HorizontalAccuracy"/>, <see cref="VerticalAccuracy"/>,
    ///     <see cref="Speed"/>, and <see cref="Course"/> via object initializer when known; they default
    ///     to <see cref="double.NaN"/> (unknown).
    /// </summary>
    /// <param name="latitude">
    ///     The latitude of the location. May range from -90.0 to 90.0.
    /// </param>
    /// <param name="longitude">
    ///     The longitude of the location. May range from -180.0 to 180.0.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     If <paramref name="latitude"/> or <paramref name="longitude"/> is out of range.
    /// </exception>
    public FullGeoCoordinate(double latitude = double.NaN, double longitude = double.NaN)
    {
        Latitude = latitude;
        Longitude = longitude;
        // Unset optional components mean "unknown" (NaN), not 0 — the backing fields would
        // otherwise default to 0.0. Object initializers may override these via init accessors.
        Altitude = double.NaN;
        HorizontalAccuracy = double.NaN;
        VerticalAccuracy = double.NaN;
        Speed = double.NaN;
        Course = double.NaN;
    }

    /// <summary>
    ///     Gets or sets the latitude of the GeoCoordinate.
    /// </summary>
    /// <returns>
    ///     Latitude of the location.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Latitude is set outside the valid range.</exception>
    public double Latitude
    {
        get;
        init
        {
            if (value is > 90.0 or < -90.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Argument `Latitude` must be in range of -90 to 90"
                );
            }

            field = value;
        }
    }

    /// <summary>
    ///     Gets or sets the longitude of the GeoCoordinate.
    /// </summary>
    /// <returns>
    ///     The longitude.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Longitude is set outside the valid range.</exception>
    public double Longitude
    {
        get;
        init
        {
            if (value is > 180.0 or < -180.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Argument `Longitude` must be in range of -180 to 180"
                );
            }

            field = value;
        }
    }

    /// <summary>
    ///     Gets or sets the accuracy of the latitude and longitude that is given by the GeoCoordinate, in meters.
    /// </summary>
    /// <returns>
    ///     The accuracy of the latitude and longitude, in meters.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">HorizontalAccuracy is set outside the valid range.</exception>
    public double HorizontalAccuracy
    {
        get;
        init
        {
            if (value < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Argument `HorizontalAccuracy` must be non negative"
                );
            }

            field = value == 0.0 ? double.NaN : value;
        }
    }

    /// <summary>
    ///     Gets or sets the accuracy of the altitude given by the GeoCoordinate, in meters.
    /// </summary>
    /// <returns>
    ///     The accuracy of the altitude, in meters.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">VerticalAccuracy is set outside the valid range.</exception>
    public double VerticalAccuracy
    {
        get;
        init
        {
            if (value < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Argument `VerticalAccuracy` must be non negative"
                );
            }

            field = value == 0.0 ? double.NaN : value;
        }
    }

    /// <summary>
    ///     Gets or sets the speed in meters per second.
    /// </summary>
    /// <returns>
    ///     The speed in meters per second. The speed must be greater than or equal to zero, or Double.NaN.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Speed is set outside the valid range.</exception>
    public double Speed
    {
        get;
        init
        {
            if (value < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Argument `Speed` must be non negative");
            }

            field = value;
        }
    }

    /// <summary>
    ///     Gets or sets the heading in degrees, relative to true north.
    /// </summary>
    /// <returns>
    ///     The heading in degrees, relative to true north.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Course is set outside the valid range.</exception>
    public double Course
    {
        get;
        init
        {
            if (value is < 0.0 or > 360.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Argument `Course` must be in range 0 to 360");
            }

            field = value;
        }
    }

    /// <summary>
    ///     Gets a value that indicates whether the GeoCoordinate does not contain latitude or longitude data.
    /// </summary>
    /// <returns>
    ///     true if the GeoCoordinate does not contain latitude or longitude data; otherwise, false.
    /// </returns>
    public bool IsUnknown => Equals(Unknown);

    /// <summary>
    ///     Gets the altitude of the GeoCoordinate, in meters.
    /// </summary>
    /// <returns>
    ///     The altitude, in meters.
    /// </returns>
    public double Altitude { get; init; }

    /// <summary>
    ///     Returns the distance between the latitude and longitude coordinates that are specified by this GeoCoordinate and
    ///     another specified GeoCoordinate.
    /// </summary>
    /// <returns>
    ///     The distance between the two coordinates, in meters.
    /// </returns>
    /// <param name="other">The GeoCoordinate for the location to calculate the distance to.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when the latitude or longitude of either this coordinate or <paramref name="other"/> is
    ///     <see cref="double.NaN"/>.
    /// </exception>
    public double GetDistanceTo(FullGeoCoordinate other)
    {
        Argument.IsNotNaN(Latitude);
        Argument.IsNotNaN(Longitude);
        Argument.IsNotNaN(other.Latitude);
        Argument.IsNotNaN(other.Longitude);

        var d1 = Latitude * (Math.PI / 180.0);
        var num1 = Longitude * (Math.PI / 180.0);
        var d2 = other.Latitude * (Math.PI / 180.0);
        var num2 = (other.Longitude * (Math.PI / 180.0)) - num1;

        // sin(x)^2 via a multiply instead of Math.Pow(..., 2.0): same result, avoids the general pow() path.
        var sinLat = Math.Sin((d2 - d1) / 2.0);
        var sinLon = Math.Sin(num2 / 2.0);
        var d3 = (sinLat * sinLat) + (Math.Cos(d1) * Math.Cos(d2) * sinLon * sinLon);

        // 6_376_500 m: Earth radius constant carried over verbatim from the original System.Device.Location /
        // GeoCoordinatePortable implementation (kept for parity); not the WGS-84 mean radius of 6_371_000 m.
        return 6376500.0 * (2.0 * Math.Atan2(Math.Sqrt(d3), Math.Sqrt(1.0 - d3)));
    }

    /// <summary>
    ///     Determines whether two GeoCoordinate objects refer to the same location.
    /// </summary>
    /// <returns>
    ///     true, if the GeoCoordinate objects are determined to be equivalent; otherwise, false.
    /// </returns>
    /// <param name="left">The first GeoCoordinate to compare.</param>
    /// <param name="right">The second GeoCoordinate to compare.</param>
    public static bool operator ==(FullGeoCoordinate? left, FullGeoCoordinate? right)
    {
        return left?.Equals(right) ?? (right is null);
    }

    /// <summary>
    ///     Determines whether two GeoCoordinate objects correspond to different locations.
    /// </summary>
    /// <returns>
    ///     true, if the GeoCoordinate objects are determined to be different; otherwise, false.
    /// </returns>
    /// <param name="left">The first GeoCoordinate to compare.</param>
    /// <param name="right">The second GeoCoordinate to compare.</param>
    public static bool operator !=(FullGeoCoordinate? left, FullGeoCoordinate? right)
    {
        return !(left == right);
    }

    /// <summary>
    ///     Determines if the GeoCoordinate object is equivalent to the parameter, based solely on latitude and longitude.
    /// </summary>
    /// <returns>
    ///     true if the GeoCoordinate objects are equal; otherwise, false.
    /// </returns>
    /// <param name="other">The GeoCoordinate object to compare to the calling object.</param>
    public bool Equals(FullGeoCoordinate? other)
    {
        if (other is null)
        {
            return false;
        }

        var num = Latitude;

        if (!num.Equals(other.Latitude))
        {
            return false;
        }

        num = Longitude;

        return num.Equals(other.Longitude);
    }

    /// <summary>
    ///     Determines if a specified GeoCoordinate is equal to the current GeoCoordinate, based solely on latitude and
    ///     longitude.
    /// </summary>
    /// <returns>
    ///     true, if the GeoCoordinate objects are equal; otherwise, false.
    /// </returns>
    /// <param name="obj">The object to compare the GeoCoordinate to.</param>
    public override bool Equals(object? obj)
    {
        return Equals(obj as FullGeoCoordinate);
    }

    /// <summary>
    ///     Serves as a hash function for the GeoCoordinate.
    /// </summary>
    /// <returns>
    ///     A hash code for the current GeoCoordinate.
    /// </returns>
    public override int GetHashCode()
    {
        // HashCode.Combine is order-sensitive, so swapped coordinates (e.g. 10,20 vs 20,10) do not collide,
        // unlike the previous XOR which hashed them identically.
        return HashCode.Combine(Latitude, Longitude);
    }

    /// <summary>
    ///     Returns a string that contains the latitude and longitude.
    /// </summary>
    /// <returns>
    ///     A string that contains the latitude and longitude, separated by a comma.
    /// </returns>
    public override string ToString()
    {
        return this == Unknown
            ? "Unknown"
            : $"{Latitude.ToString("G", CultureInfo.InvariantCulture)}, {Longitude.ToString("G", CultureInfo.InvariantCulture)}";
    }
}
