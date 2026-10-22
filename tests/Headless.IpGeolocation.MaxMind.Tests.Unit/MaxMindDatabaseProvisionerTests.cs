// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.IpGeolocation;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class MaxMindDatabaseProvisionerTests : MaxMindTestBase
{
    private static readonly DateTimeOffset _ReleaseDate = new(2026, 10, 10, 8, 30, 44, TimeSpan.Zero);

    [Fact]
    public async Task should_download_every_missing_edition_when_provisioned()
    {
        // given
        Handler.Publish(TestDatabases.CityEdition, TestDatabases.Archive(TestDatabases.CityEdition), _ReleaseDate);
        Handler.Publish(TestDatabases.AsnEdition, TestDatabases.Archive(TestDatabases.AsnEdition), _ReleaseDate);
        var provider = BuildProvider(withCredentials: true);
        var resource = _Resource(provider);

        // when
        await resource.ProvisionAsync(AbortToken);

        // then
        resource.Kind.Should().Be("MaxMind");
        resource.Name.Should().Be("databases");
        Handler.Downloads.Should().Be(2);
        File.Exists(_PathOf(TestDatabases.CityEdition)).Should().BeTrue();
        File.Exists(_PathOf(TestDatabases.AsnEdition)).Should().BeTrue();
        await resource.Invoking(r => r.VerifyAsync(AbortToken)).Should().NotThrowAsync();
        provider.GetRequiredService<IIpGeolocator>().Locate(TestDatabases.BoxfordAddress)!.City.Should().Be("Boxford");
    }

    [Fact]
    public async Task should_skip_an_edition_whose_release_is_unchanged_when_provisioned()
    {
        // given
        var installed = TestDatabases.Install(DatabaseDirectory, TestDatabases.AsnEdition);
        File.SetLastWriteTimeUtc(installed, _ReleaseDate.UtcDateTime);
        Handler.Publish(TestDatabases.AsnEdition, TestDatabases.Archive(TestDatabases.AsnEdition), _ReleaseDate);
        var resource = _Resource(BuildProvider(options => options.LocationEditionId = null, withCredentials: true));

        // when
        await resource.ProvisionAsync(AbortToken);

        // then
        Handler.Checks.Should().Be(1);
        Handler.Downloads.Should().Be(0);
    }

    [Fact]
    public async Task should_fail_naming_the_edition_when_provisioning_cannot_download_it()
    {
        // given
        Handler.Publish(TestDatabases.CityEdition, TestDatabases.Archive(TestDatabases.CityEdition), _ReleaseDate);
        var resource = _Resource(BuildProvider(withCredentials: true));

        // when
        var act = () => resource.ProvisionAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*GeoLite2-ASN*");
        File.Exists(_PathOf(TestDatabases.CityEdition)).Should().BeTrue();
    }

    [Fact]
    public async Task should_not_contact_maxmind_when_provisioned_without_credentials()
    {
        // given
        var resource = _Resource(BuildProvider());

        // when
        await resource.ProvisionAsync(AbortToken);

        // then
        Handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_verify_naming_only_the_missing_edition()
    {
        // given
        TestDatabases.Install(DatabaseDirectory, TestDatabases.CityEdition);
        var resource = _Resource(BuildProvider(withCredentials: true));

        // when
        var act = () => resource.VerifyAsync(AbortToken);

        // then
        var error = await act.Should().ThrowAsync<ResourceProvisioningException>();
        error.Which.Message.Should().Contain("missing: GeoLite2-ASN").And.NotContain(TestDatabases.CityEdition);
        Handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_verify_when_a_database_file_is_unreadable()
    {
        // given
        Directory.CreateDirectory(DatabaseDirectory);
        await File.WriteAllTextAsync(_PathOf(TestDatabases.AsnEdition), "not a database", AbortToken);
        var resource = _Resource(BuildProvider(options => options.LocationEditionId = null));

        // when
        var act = () => resource.VerifyAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*not a readable MaxMind database: GeoLite2-ASN*");
    }

    [Fact]
    public async Task should_delete_every_database_when_deleted()
    {
        // given
        TestDatabases.Install(DatabaseDirectory, TestDatabases.CityEdition);
        TestDatabases.Install(DatabaseDirectory, TestDatabases.AsnEdition);
        var provider = BuildProvider();
        var resource = _Resource(provider);

        // when
        await resource.DeleteAsync(AbortToken);

        // then
        Directory.GetFiles(DatabaseDirectory).Should().BeEmpty();
        provider.GetRequiredService<IIpGeolocator>().Locate(TestDatabases.BoxfordAddress).Should().BeNull();
        await resource.Invoking(r => r.VerifyAsync(AbortToken)).Should().ThrowAsync<ResourceProvisioningException>();
    }

    [Fact]
    public void should_report_purge_as_unsupported()
    {
        // given
        var resource = _Resource(BuildProvider());

        // then
        resource.CanPurge.Should().BeFalse();
        resource.CanDelete.Should().BeTrue();
    }

    [Fact]
    public async Task should_report_each_edition_build_date_in_status()
    {
        // given
        TestDatabases.Install(DatabaseDirectory, TestDatabases.CityEdition);
        var resource = _Resource(BuildProvider());

        // when
        var status = await resource.GetStatusAsync(AbortToken);

        // then
        status.Select(s => s.Key).Should().Equal(TestDatabases.CityEdition, TestDatabases.AsnEdition, "downloads");
        status[0].Value.Should().MatchRegex(@"^built \d{4}-\d{2}-\d{2}$");
        status[1].Value.Should().Be("missing");
        status[2].Value.Should().Be("disabled");
    }

    private string _PathOf(string edition) => Path.Combine(DatabaseDirectory, edition + ".mmdb");

    private static IResourceProvisioner _Resource(IServiceProvider provider) =>
        provider
            .GetServices<IResourceProvisioner>()
            .Single(r => string.Equals(r.Kind, "MaxMind", StringComparison.Ordinal));
}
