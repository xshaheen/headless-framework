// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IpGeolocation;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class MaxMindIpGeolocatorTests : MaxMindTestBase
{
    [Fact]
    public void should_return_city_location_when_address_is_in_the_city_database()
    {
        // given
        TestDatabases.Install(DatabaseDirectory, TestDatabases.CityEdition);
        TestDatabases.Install(DatabaseDirectory, TestDatabases.AsnEdition);
        var geolocator = BuildProvider().GetRequiredService<IIpGeolocator>();

        // when
        var location = geolocator.Locate(TestDatabases.BoxfordAddress);

        // then
        location.Should().NotBeNull();
        location.CountryCode.Should().Be("GB");
        location.CountryName.Should().Be("United Kingdom");
        location.SubdivisionCode.Should().Be("WBK");
        location.SubdivisionName.Should().Be("West Berkshire");
        location.City.Should().Be("Boxford");
        location.PostalCode.Should().Be("OX1");
        location.Latitude.Should().Be(51.75);
        location.Longitude.Should().Be(-1.25);
        location.AccuracyRadiusKilometers.Should().Be(100);
        location.TimeZone.Should().Be("Europe/London");
        location.AutonomousSystemNumber.Should().BeNull();
    }

    [Fact]
    public void should_return_autonomous_system_when_address_is_only_in_the_asn_database()
    {
        // given
        TestDatabases.Install(DatabaseDirectory, TestDatabases.CityEdition);
        TestDatabases.Install(DatabaseDirectory, TestDatabases.AsnEdition);
        var geolocator = BuildProvider().GetRequiredService<IIpGeolocator>();

        // when
        var location = geolocator.Locate(TestDatabases.TelstraAddress);

        // then
        location.Should().NotBeNull();
        location.AutonomousSystemNumber.Should().Be(1221);
        location.AutonomousSystemOrganization.Should().Be("Telstra Pty Ltd");
        location.CountryCode.Should().BeNull();
    }

    [Fact]
    public void should_return_null_when_no_database_knows_the_address()
    {
        // given
        TestDatabases.Install(DatabaseDirectory, TestDatabases.CityEdition);
        TestDatabases.Install(DatabaseDirectory, TestDatabases.AsnEdition);
        var geolocator = BuildProvider().GetRequiredService<IIpGeolocator>();

        // when
        var location = geolocator.Locate(TestDatabases.PrivateAddress);

        // then
        location.Should().BeNull();
    }

    [Fact]
    public void should_return_country_only_when_the_location_edition_is_a_country_database()
    {
        // given
        TestDatabases.Install(DatabaseDirectory, TestDatabases.CountryEdition);
        var geolocator = BuildProvider(options =>
            {
                options.LocationEditionId = TestDatabases.CountryEdition;
                options.AsnEditionId = null;
            })
            .GetRequiredService<IIpGeolocator>();

        // when
        var location = geolocator.Locate(TestDatabases.BoxfordAddress);

        // then
        location.Should().Be(new IpLocation { CountryCode = "GB", CountryName = "United Kingdom" });
    }

    [Fact]
    public void should_return_null_when_the_database_files_are_missing()
    {
        // given
        var geolocator = BuildProvider().GetRequiredService<IIpGeolocator>();

        // when
        var location = geolocator.Locate(TestDatabases.BoxfordAddress);

        // then
        location.Should().BeNull();
    }

    [Fact]
    public void should_return_null_when_a_database_file_is_corrupt()
    {
        // given
        Directory.CreateDirectory(DatabaseDirectory);
        File.WriteAllText(Path.Combine(DatabaseDirectory, TestDatabases.CityEdition + ".mmdb"), "not a database");
        var geolocator = BuildProvider(options => options.AsnEditionId = null).GetRequiredService<IIpGeolocator>();

        // when
        var location = geolocator.Locate(TestDatabases.BoxfordAddress);

        // then
        location.Should().BeNull();
    }

    [Fact]
    public void should_throw_when_the_address_is_null()
    {
        // given
        var geolocator = BuildProvider().GetRequiredService<IIpGeolocator>();

        // when
        var act = () => geolocator.Locate(null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }
}
