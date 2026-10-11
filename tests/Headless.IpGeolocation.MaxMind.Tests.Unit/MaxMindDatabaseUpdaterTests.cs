// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.IpGeolocation;
using Headless.IpGeolocation.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class MaxMindDatabaseUpdaterTests : MaxMindTestBase
{
    private static readonly DateTimeOffset _ReleaseDate = new(2026, 10, 10, 8, 30, 44, TimeSpan.Zero);

    [Fact]
    public async Task should_download_and_load_the_database_when_none_exists()
    {
        // given
        Handler.Publish(TestDatabases.AsnEdition, TestDatabases.Archive(TestDatabases.AsnEdition), _ReleaseDate);
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);

        // when
        var succeeded = await _Updater(provider).CheckAllAsync(AbortToken);

        // then
        succeeded.Should().BeTrue();
        provider
            .GetRequiredService<IIpGeolocator>()
            .Locate(TestDatabases.TelstraAddress)!
            .AutonomousSystemNumber.Should()
            .Be(1221);
        var path = Path.Combine(DatabaseDirectory, TestDatabases.AsnEdition + ".mmdb");
        File.GetLastWriteTimeUtc(path).Should().Be(_ReleaseDate.UtcDateTime);
        Directory.GetFiles(DatabaseDirectory).Should().ContainSingle();
    }

    [Fact]
    public async Task should_not_download_again_when_the_release_is_unchanged()
    {
        // given
        Handler.Publish(TestDatabases.AsnEdition, TestDatabases.Archive(TestDatabases.AsnEdition), _ReleaseDate);
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);
        var updater = _Updater(provider);
        await updater.CheckAllAsync(AbortToken);

        // when
        var succeeded = await updater.CheckAllAsync(AbortToken);

        // then
        succeeded.Should().BeTrue();
        Handler.Downloads.Should().Be(1);
    }

    [Fact]
    public async Task should_replace_the_loaded_database_when_a_newer_release_is_published()
    {
        // given
        TestDatabases.Install(DatabaseDirectory, TestDatabases.CityEdition);
        File.SetLastWriteTimeUtc(
            Path.Combine(DatabaseDirectory, TestDatabases.CityEdition + ".mmdb"),
            _ReleaseDate.UtcDateTime.AddDays(-7)
        );
        Handler.Publish(TestDatabases.CityEdition, TestDatabases.Archive(TestDatabases.CityEdition), _ReleaseDate);
        var provider = BuildProvider(options => options.AsnEditionId = null, withCredentials: true);

        // when
        var succeeded = await _Updater(provider).CheckAllAsync(AbortToken);

        // then
        succeeded.Should().BeTrue();
        Handler.Downloads.Should().Be(1);
        provider.GetRequiredService<IIpGeolocator>().Locate(TestDatabases.BoxfordAddress)!.City.Should().Be("Boxford");
    }

    [Fact]
    public async Task should_keep_the_current_database_when_the_checksum_does_not_match()
    {
        // given
        var installed = TestDatabases.Install(DatabaseDirectory, TestDatabases.CityEdition);
        File.SetLastWriteTimeUtc(installed, _ReleaseDate.UtcDateTime.AddDays(-7));
        Handler.Publish(
            TestDatabases.CityEdition,
            TestDatabases.Archive(TestDatabases.CityEdition),
            _ReleaseDate,
            sha256: new string('0', 64)
        );
        var provider = BuildProvider(options => options.AsnEditionId = null, withCredentials: true);

        // when
        var succeeded = await _Updater(provider).CheckAllAsync(AbortToken);

        // then
        succeeded.Should().BeFalse();
        File.GetLastWriteTimeUtc(installed).Should().Be(_ReleaseDate.UtcDateTime.AddDays(-7));
        Directory.GetFiles(DatabaseDirectory).Should().ContainSingle();
        provider.GetRequiredService<IIpGeolocator>().Locate(TestDatabases.BoxfordAddress)!.City.Should().Be("Boxford");
    }

    [Fact]
    public async Task should_reject_a_download_that_is_not_a_database()
    {
        // given
        var archive = TestDatabases.Archive(TestDatabases.AsnEdition, Encoding.UTF8.GetBytes("not a database"));
        Handler.Publish(TestDatabases.AsnEdition, archive, _ReleaseDate);
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);

        // when
        var succeeded = await _Updater(provider).CheckAllAsync(AbortToken);

        // then
        succeeded.Should().BeFalse();
        Directory.GetFiles(DatabaseDirectory).Should().BeEmpty();
        provider.GetRequiredService<IIpGeolocator>().Locate(TestDatabases.TelstraAddress).Should().BeNull();
    }

    [Fact]
    public async Task should_reject_an_archive_without_a_database()
    {
        // given
        var archive = TestDatabases.Archive(TestDatabases.AsnEdition, entryName: "GeoLite2-ASN_20261010/LICENSE.txt");
        Handler.Publish(TestDatabases.AsnEdition, archive, _ReleaseDate);
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);

        // when
        var succeeded = await _Updater(provider).CheckAllAsync(AbortToken);

        // then
        succeeded.Should().BeFalse();
        Directory.GetFiles(DatabaseDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_reject_a_download_when_the_published_checksum_is_malformed()
    {
        // given
        Handler.Publish(
            TestDatabases.AsnEdition,
            TestDatabases.Archive(TestDatabases.AsnEdition),
            _ReleaseDate,
            sha256: "not-a-digest"
        );
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);

        // when
        var succeeded = await _Updater(provider).CheckAllAsync(AbortToken);

        // then
        succeeded.Should().BeFalse();
        Handler.Downloads.Should().Be(0);
    }

    [Fact]
    public async Task should_check_again_when_the_update_interval_passes()
    {
        // given
        Handler.Publish(TestDatabases.AsnEdition, TestDatabases.Archive(TestDatabases.AsnEdition), _ReleaseDate);
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);
        var updater = _Updater(provider);
        var geolocator = provider.GetRequiredService<IIpGeolocator>();
        await updater.StartAsync(AbortToken);
        await _WaitUntil(() => geolocator.Locate(TestDatabases.TelstraAddress) is not null);

        // when
        Clock.Advance(TimeSpan.FromHours(12));
        await _WaitUntil(() => Handler.Checks == 2);
        await updater.StopAsync(AbortToken);

        // then
        Handler.Downloads.Should().Be(1);
    }

    [Fact]
    public async Task should_report_failure_when_the_edition_is_not_published()
    {
        // given
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);

        // when
        var succeeded = await _Updater(provider).CheckAllAsync(AbortToken);

        // then
        succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_send_the_account_credentials_with_every_request()
    {
        // given
        Handler.Publish(TestDatabases.AsnEdition, TestDatabases.Archive(TestDatabases.AsnEdition), _ReleaseDate);
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);

        // when
        await _Updater(provider).CheckAllAsync(AbortToken);

        // then
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("42:test-license-key"));
        Handler.Requests.Should().NotBeEmpty();
        Handler
            .Requests.Should()
            .AllSatisfy(request =>
            {
                request.Headers.Authorization!.Scheme.Should().Be("Basic");
                request.Headers.Authorization.Parameter.Should().Be(expected);
            });
    }

    [Fact]
    public async Task should_check_for_updates_when_the_host_starts_with_credentials()
    {
        // given
        Handler.Publish(TestDatabases.AsnEdition, TestDatabases.Archive(TestDatabases.AsnEdition), _ReleaseDate);
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);
        var updater = _Updater(provider);
        var geolocator = provider.GetRequiredService<IIpGeolocator>();

        // when
        await updater.StartAsync(AbortToken);
        await _WaitUntil(() => geolocator.Locate(TestDatabases.TelstraAddress) is not null);
        await updater.StopAsync(AbortToken);

        // then
        Handler.Downloads.Should().Be(1);
    }

    [Fact]
    public async Task should_not_contact_maxmind_when_no_credentials_are_configured()
    {
        // given
        TestDatabases.Install(DatabaseDirectory, TestDatabases.AsnEdition);
        var provider = BuildProvider(options => options.LocationEditionId = null);
        var updater = _Updater(provider);

        // when
        await updater.StartAsync(AbortToken);
        await updater.ExecuteTask!;
        await updater.StopAsync(AbortToken);

        // then
        Handler.Requests.Should().BeEmpty();
        provider.GetRequiredService<IIpGeolocator>().Locate(TestDatabases.TelstraAddress).Should().NotBeNull();
    }

    private static MaxMindDatabaseUpdater _Updater(IServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<MaxMindDatabaseUpdater>().Single();

    private static async Task _WaitUntil(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}
