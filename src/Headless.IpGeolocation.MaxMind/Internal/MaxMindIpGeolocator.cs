// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Checks;
using MaxMind.GeoIP2.Responses;

namespace Headless.IpGeolocation.Internal;

internal sealed class MaxMindIpGeolocator(MaxMindDatabases databases) : IIpGeolocator
{
    public IpLocation? Locate(IPAddress address)
    {
        Argument.IsNotNull(address);

        var location = _LocateLocation(databases.Location, address);
        var asn = _LocateAsn(databases.Asn, address);

        if (location is null && asn is null)
        {
            return null;
        }

        return (location ?? new IpLocation()) with
        {
            AutonomousSystemNumber = asn?.AutonomousSystemNumber,
            AutonomousSystemOrganization = asn?.AutonomousSystemOrganization,
        };
    }

    private static IpLocation? _LocateLocation(MaxMindDatabase? database, IPAddress address)
    {
        if (database is null)
        {
            return null;
        }

        if (database.HasCityData)
        {
            return database.Reader.TryCity(address, out var city) && city is not null ? _FromCity(city) : null;
        }

        return database.Reader.TryCountry(address, out var country) && country is not null
            ? _FromCountry(country)
            : null;
    }

    private static AsnResponse? _LocateAsn(MaxMindDatabase? database, IPAddress address)
    {
        return database is not null && database.Reader.TryAsn(address, out var asn) ? asn : null;
    }

    private static IpLocation _FromCountry(AbstractCountryResponse response)
    {
        return new IpLocation { CountryCode = response.Country.IsoCode, CountryName = response.Country.Name };
    }

    private static IpLocation _FromCity(CityResponse response)
    {
        var subdivision = response.MostSpecificSubdivision;

        return _FromCountry(response) with
        {
            SubdivisionCode = subdivision.IsoCode,
            SubdivisionName = subdivision.Name,
            City = response.City.Name,
            PostalCode = response.Postal.Code,
            Latitude = response.Location.Latitude,
            Longitude = response.Location.Longitude,
            AccuracyRadiusKilometers = response.Location.AccuracyRadius,
            TimeZone = response.Location.TimeZone,
        };
    }
}
