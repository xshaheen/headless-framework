using Framework.Kernel.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

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

    private readonly double _course;
    private readonly double _horizontalAccuracy;
    private readonly double _latitude;
    private readonly double _longitude;
    private readonly double _speed;
    private readonly double _verticalAccuracy;

    /// <summary>
    ///     Initializes a new instance of the GeoCoordinate class from latitude, longitude, altitude, horizontal accuracy,
    ///     vertical accuracy, speed, and course.
    /// </summary>
    /// <param name="latitude">
    ///     The latitude of the location. May range from -90.0 to 90.0.
    /// </param>
    /// <param name="longitude">
    ///     The longitude of the location. May range from -180.0 to 180.0.
    /// </param>
    /// <param name="altitude">
    ///     The altitude in meters. May be negative, 0, positive, or Double.NaN, if unknown.
    /// </param>
    /// <param name="horizontalAccuracy">
    ///     The accuracy of the latitude and longitude coordinates, in meters. Must be greater
    ///     than or equal to 0. If a value of 0 is supplied to this constructor, the HorizontalAccuracy property will be set to
    ///     Double.NaN.
    /// </param>
    /// <param name="verticalAccuracy">
    ///     The accuracy of the altitude, in meters. Must be greater than or equal to 0. If a value
    ///     of 0 is supplied to this constructor, the VerticalAccuracy property will be set to Double.NaN.
    /// </param>
    /// <param name="speed">
    ///     The speed measured in meters per second. May be negative, 0, positive, or Double.NaN, if unknown.
    ///     A negative speed can indicate moving in reverse.
    /// </param>
    /// <param name="course">
    ///     The direction of travel, rather than orientation. This parameter is measured in degrees relative
    ///     to true north. Must range from 0 to 360.0, or be Double.NaN.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     If latitude, longitude, horizontalAccuracy, verticalAccuracy, course is out of range.
    /// </exception>
    public FullGeoCoordinate(
        double latitude = double.NaN,
        double longitude = double.NaN,
        double altitude = double.NaN,
        double horizontalAccuracy = double.NaN,
        double verticalAccuracy = double.NaN,
        double speed = double.NaN,
        double course = double.NaN
    )
    {
        Latitude = latitude;
        Longitude = longitude;
        Altitude = altitude;
        HorizontalAccuracy = horizontalAccuracy;
        VerticalAccuracy = verticalAccuracy;
        Speed = speed;
        Course = course;
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
        get { return _latitude; }
        init
        {
            if (value is > 90.0 or < -90.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Argument `Latitude` must be in range of -90 to 90"
                );
            }

            _latitude = value;
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
        get { return _longitude; }
        init
        {
            if (value is > 180.0 or < -180.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Argument `Longitude` must be in range of -180 to 180"
                );
            }

            _longitude = value;
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
        get { return _horizontalAccuracy; }
        init
        {
            if (value < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Argument `HorizontalAccuracy` must be non negative"
                );
            }

            _horizontalAccuracy = value == 0.0 ? double.NaN : value;
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
        get { return _verticalAccuracy; }
        init
        {
            if (value < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Argument `VerticalAccuracy` must be non negative"
                );
            }

            _verticalAccuracy = value == 0.0 ? double.NaN : value;
        }
    }

    /// <summary>
    ///     Gets or sets the speed in meters per second.
    /// </summary>
    /// <returns>
    ///     The speed in meters per second. The speed must be greater than or equal to zero, or Double.NaN.
    /// </returns>
    /// <exception cref="System.ArgumentOutOfRangeException">Speed is set outside the valid range.</exception>
    public double Speed
    {
        get { return _speed; }
        init
        {
            if (value < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Argument `Speed` must be non negative");
            }

            _speed = value;
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
        get { return _course; }
        init
        {
            if (value is < 0.0 or > 360.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), @"Argument `Course` must be in range 0 to 360");
            }

            _course = value;
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
    public double Altitude { get; set; }

    /// <summary>
    ///     Returns the distance between the latitude and longitude coordinates that are specified by this GeoCoordinate and
    ///     another specified GeoCoordinate.
    /// </summary>
    /// <returns>
    ///     The distance between the two coordinates, in meters.
    /// </returns>
    /// <param name="other">The GeoCoordinate for the location to calculate the distance to.</param>
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
        var d3 =
            Math.Pow(Math.Sin((d2 - d1) / 2.0), 2.0)
            + (Math.Cos(d1) * Math.Cos(d2) * Math.Pow(Math.Sin(num2 / 2.0), 2.0));

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
        return left?.Equals(right) ?? right is null;
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
        return Latitude.GetHashCode() ^ Longitude.GetHashCode();
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
