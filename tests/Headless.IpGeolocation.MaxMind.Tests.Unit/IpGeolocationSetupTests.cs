// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IpGeolocation;
using Headless.IpGeolocation.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class IpGeolocationSetupTests : MaxMindTestBase
{
    [Fact]
    public void should_register_the_maxmind_geolocator_when_use_maxmind()
    {
        // given
        var provider = BuildProvider();

        // when
        var geolocator = provider.GetRequiredService<IIpGeolocator>();

        // then
        geolocator.Should().BeOfType<MaxMindIpGeolocator>();
    }

    [Fact]
    public void should_bind_options_from_configuration_when_use_maxmind_with_configuration()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["DatabaseDirectory"] = DatabaseDirectory,
                    ["AsnEditionId"] = "",
                    ["UpdateCheckInterval"] = "06:00:00",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessIpGeolocation(geo => geo.UseMaxMind(configuration));
        using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<MaxMindOptions>>().Value;

        // then
        options.DatabaseDirectory.Should().Be(DatabaseDirectory);
        options.UpdateCheckInterval.Should().Be(TimeSpan.FromHours(6));
        options.LocationEditionId.Should().Be("GeoLite2-City");
        provider.GetRequiredService<MaxMindDatabases>().Editions.Should().Equal("GeoLite2-City");
    }

    [Fact]
    public void should_configure_options_from_services_when_use_maxmind_with_service_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new DatabaseLocation(DatabaseDirectory));
        services.AddHeadlessIpGeolocation(geo =>
            geo.UseMaxMind((options, sp) => options.DatabaseDirectory = sp.GetRequiredService<DatabaseLocation>().Path)
        );
        using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<MaxMindOptions>>().Value;

        // then
        options.DatabaseDirectory.Should().Be(DatabaseDirectory);
    }

    [Fact]
    public void should_throw_when_no_provider_is_chosen()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddHeadlessIpGeolocation(_ => { });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*exactly one provider*");
    }

    [Fact]
    public void should_throw_when_two_providers_are_chosen()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessIpGeolocation(geo =>
                geo.UseMaxMind(o => o.DatabaseDirectory = "a").UseMaxMind(o => o.DatabaseDirectory = "b")
            );

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*");
    }

    [Fact]
    public void should_throw_when_registered_twice()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessIpGeolocation(geo => geo.UseMaxMind(o => o.DatabaseDirectory = "a"));

        // when
        var act = () => services.AddHeadlessIpGeolocation(geo => geo.UseMaxMind(o => o.DatabaseDirectory = "a"));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*already called*");
    }

    [Theory]
    [InlineData("account-only")]
    [InlineData("no-directory")]
    [InlineData("no-editions")]
    [InlineData("check-too-often")]
    [InlineData("insecure-endpoint")]
    public void should_reject_invalid_options(string scenario)
    {
        // given
        var provider = BuildProvider(options =>
        {
            switch (scenario)
            {
                case "account-only":
                    options.AccountId = "42";
                    break;
                case "no-directory":
                    options.DatabaseDirectory = "";
                    break;
                case "no-editions":
                    options.LocationEditionId = null;
                    options.AsnEditionId = null;
                    break;
                case "check-too-often":
                    options.UpdateCheckInterval = TimeSpan.FromMinutes(5);
                    break;
                case "insecure-endpoint":
                    options.DownloadEndpoint = "http://download.example.com/";
                    break;
            }
        });

        // when
        var act = () => provider.GetRequiredService<IOptions<MaxMindOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }
}

internal sealed record DatabaseLocation(string Path);
