// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.IpGeolocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class MaxMindDatabaseWarmupTests : MaxMindTestBase
{
    private static readonly DateTimeOffset _ReleaseDate = new(2026, 10, 10, 8, 30, 44, TimeSpan.Zero);

    [Fact]
    public async Task should_not_report_ready_until_the_first_download_lands_when_a_database_is_missing()
    {
        // given
        Handler.Publish(TestDatabases.AsnEdition, TestDatabases.Archive(TestDatabases.AsnEdition), _ReleaseDate);
        Handler.DownloadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);
        var geolocator = provider.GetRequiredService<IIpGeolocator>();
        var lifecycle = _LifecycleServices(provider);

        // when
        await _StartAsync(lifecycle);
        await WaitUntilAsync(() => Handler.Downloads == 1);
        var readyMidDownload = await _IsReadyAsync(provider);
        var locatedMidDownload = geolocator.Locate(TestDatabases.TelstraAddress);
        Handler.DownloadGate.SetResult();
        await provider.WaitForWarmupAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        await _StopAsync(lifecycle);

        // then
        readyMidDownload.Should().BeFalse();
        locatedMidDownload.Should().BeNull();
        geolocator.Locate(TestDatabases.TelstraAddress)!.AutonomousSystemNumber.Should().Be(1221);
        Handler.Downloads.Should().Be(1);
    }

    [Fact]
    public async Task should_report_ready_at_once_without_downloading_when_every_database_is_present()
    {
        // given
        TestDatabases.Install(DatabaseDirectory, TestDatabases.AsnEdition);
        Handler.Publish(TestDatabases.AsnEdition, TestDatabases.Archive(TestDatabases.AsnEdition), _ReleaseDate);
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);
        var lifecycle = _LifecycleServices(provider);

        // when
        await _StartAsync(lifecycle);

        // The readiness checks run on the warm-up runner's own thread, so wait briefly instead of reading at once.
        await provider.WaitForWarmupAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        var ready = await _IsReadyAsync(provider);
        await _StopAsync(lifecycle);

        // then
        ready.Should().BeTrue();
        _Warmup(provider).GatesReadiness.Should().BeFalse();
        Handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_ready_without_downloading_when_no_credentials_are_configured()
    {
        // given
        var provider = BuildProvider(options => options.LocationEditionId = null);
        var lifecycle = _LifecycleServices(provider);

        // when
        await _StartAsync(lifecycle);

        // The readiness checks run on the warm-up runner's own thread, so wait briefly instead of reading at once.
        await provider.WaitForWarmupAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        var ready = await _IsReadyAsync(provider);
        await _StopAsync(lifecycle);

        // then
        ready.Should().BeTrue();
        Handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_naming_the_edition_when_the_first_download_fails()
    {
        // given
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);
        var warmup = _Warmup(provider);

        // when
        var act = () => warmup.WarmUpAsync(AbortToken);

        // then
        warmup.GatesReadiness.Should().BeTrue();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*GeoLite2-ASN*");
    }

    [Fact]
    public async Task should_not_download_again_within_the_retry_delay_when_a_download_failed()
    {
        // given
        _PublishCorruptAsn();
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);
        var warmup = _Warmup(provider);
        await warmup.Invoking(w => w.WarmUpAsync(AbortToken)).Should().ThrowAsync<InvalidOperationException>();
        var requestsAfterTheFailure = Handler.Requests.Count;

        // when
        Clock.Advance(TimeSpan.FromMinutes(29));
        var act = () => warmup.WarmUpAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*GeoLite2-ASN*does not match*");
        Handler.Requests.Count.Should().Be(requestsAfterTheFailure);
        Handler.Downloads.Should().Be(1);
    }

    [Fact]
    public async Task should_download_again_once_the_retry_delay_passes_after_a_failed_download()
    {
        // given
        _PublishCorruptAsn();
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);
        var warmup = _Warmup(provider);
        await warmup.Invoking(w => w.WarmUpAsync(AbortToken)).Should().ThrowAsync<InvalidOperationException>();
        Clock.Advance(TimeSpan.FromMinutes(29));
        await warmup.Invoking(w => w.WarmUpAsync(AbortToken)).Should().ThrowAsync<InvalidOperationException>();
        Handler.Publish(TestDatabases.AsnEdition, TestDatabases.Archive(TestDatabases.AsnEdition), _ReleaseDate);

        // when
        Clock.Advance(TimeSpan.FromMinutes(1));
        await warmup.WarmUpAsync(AbortToken);

        // then
        Handler.Downloads.Should().Be(2);
        provider.GetRequiredService<IIpGeolocator>().Locate(TestDatabases.TelstraAddress).Should().NotBeNull();
    }

    [Fact]
    public async Task should_not_delay_the_next_attempt_when_only_the_release_check_failed()
    {
        // given: the edition is not published, so the release check fails before any download
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);
        var warmup = _Warmup(provider);
        await warmup.Invoking(w => w.WarmUpAsync(AbortToken)).Should().ThrowAsync<InvalidOperationException>();
        Handler.Publish(TestDatabases.AsnEdition, TestDatabases.Archive(TestDatabases.AsnEdition), _ReleaseDate);

        // when
        await warmup.WarmUpAsync(AbortToken);

        // then
        Handler.Downloads.Should().Be(1);
    }

    [Fact]
    public async Task should_let_resources_setup_download_within_the_retry_delay_after_a_failed_download()
    {
        // given
        _PublishCorruptAsn();
        var provider = BuildProvider(options => options.LocationEditionId = null, withCredentials: true);
        var warmup = _Warmup(provider);
        await warmup.Invoking(w => w.WarmUpAsync(AbortToken)).Should().ThrowAsync<InvalidOperationException>();
        Handler.Publish(TestDatabases.AsnEdition, TestDatabases.Archive(TestDatabases.AsnEdition), _ReleaseDate);
        var resource = provider
            .GetServices<IResourceProvisioner>()
            .Single(r => string.Equals(r.Kind, "MaxMind", StringComparison.Ordinal));

        // when
        await resource.ProvisionAsync(AbortToken);

        // then: the successful download also lifts the delay for the warm-up
        Handler.Downloads.Should().Be(2);
        await warmup.WarmUpAsync(AbortToken);
        Handler.Downloads.Should().Be(2);
    }

    // The checksum fails after the archive download starts, which is the failure that costs a daily download.
    private void _PublishCorruptAsn() =>
        Handler.Publish(
            TestDatabases.AsnEdition,
            TestDatabases.Archive(TestDatabases.AsnEdition),
            _ReleaseDate,
            sha256: new string('0', 64)
        );

    private static IWarmup _Warmup(IServiceProvider provider) =>
        provider
            .GetServices<IWarmup>()
            .Single(w => string.Equals(w.Name, "MaxMindDatabases", StringComparison.Ordinal));

    // The warm-up runner and the startup validation runner: driven directly, so the updater's download loop never starts.
    private static List<IHostedLifecycleService> _LifecycleServices(IServiceProvider provider) =>
        [.. provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>()];

    private static async Task<bool> _IsReadyAsync(IServiceProvider provider)
    {
        var report = await provider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                static r => string.Equals(r.Name, HeadlessHealthCheckNames.Warmup, StringComparison.Ordinal),
                AbortToken
            );

        return report.Entries[HeadlessHealthCheckNames.Warmup].Status == HealthStatus.Healthy;
    }

    private static async Task _StartAsync(List<IHostedLifecycleService> services)
    {
        foreach (var service in services)
        {
            await service.StartingAsync(AbortToken);
        }

        foreach (var service in services)
        {
            await service.StartAsync(AbortToken);
        }

        foreach (var service in services)
        {
            await service.StartedAsync(AbortToken);
        }
    }

    private static async Task _StopAsync(List<IHostedLifecycleService> services)
    {
        foreach (var service in services)
        {
            await service.StoppingAsync(AbortToken);
        }

        foreach (var service in services)
        {
            await service.StopAsync(AbortToken);
        }

        foreach (var service in services)
        {
            await service.StoppedAsync(AbortToken);
        }
    }
}
