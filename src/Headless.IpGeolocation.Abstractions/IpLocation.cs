// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.IpGeolocation;

/// <summary>The approximate location and network owner of an IP address.</summary>
/// <remarks>
/// Every value is optional: databases differ in coverage, and a country-level database leaves the city fields empty.
/// Names are in the first configured locale the database has a name for.
/// </remarks>
[PublicAPI]
public sealed record IpLocation
{
    /// <summary>The ISO 3166-1 alpha-2 country code, such as <c>EG</c>.</summary>
    public string? CountryCode { get; init; }

    /// <summary>The country name.</summary>
    public string? CountryName { get; init; }

    /// <summary>The ISO 3166-2 code of the most specific subdivision, without the country prefix, such as <c>C</c>.</summary>
    public string? SubdivisionCode { get; init; }

    /// <summary>The name of the most specific subdivision, such as a governorate, state, or province.</summary>
    public string? SubdivisionName { get; init; }

    /// <summary>The city name.</summary>
    public string? City { get; init; }

    /// <summary>The postal code.</summary>
    public string? PostalCode { get; init; }

    /// <summary>The approximate latitude in decimal degrees.</summary>
    public double? Latitude { get; init; }

    /// <summary>The approximate longitude in decimal degrees.</summary>
    public double? Longitude { get; init; }

    /// <summary>The radius in kilometers around the coordinates that contains the address.</summary>
    public int? AccuracyRadiusKilometers { get; init; }

    /// <summary>The IANA time zone, such as <c>Africa/Cairo</c>.</summary>
    public string? TimeZone { get; init; }

    /// <summary>The autonomous system number of the network that announces the address.</summary>
    public long? AutonomousSystemNumber { get; init; }

    /// <summary>The organization that owns the autonomous system, usually the ISP or hosting provider.</summary>
    public string? AutonomousSystemOrganization { get; init; }
}
