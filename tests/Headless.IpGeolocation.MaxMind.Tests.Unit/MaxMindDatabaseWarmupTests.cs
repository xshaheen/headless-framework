// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.IpGeolocation;
using Microsoft.Extensions.DependencyInjection;
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
        var runner = _WarmupRunner(provider);

        // when
        await _StartAsync(runner);
        await WaitUntilAsync(() => Handler.Downloads == 1);
        var readyMidDownload = ((IInitializer)runner).IsInitialized;
        var locatedMidDownload = geolocator.Locate(TestDatabases.TelstraAddress);
        Handler.DownloadGate.SetResult();
        await ((IInitializer)runner)
            .WaitForInitializationAsync(AbortToken)
            .WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        await _StopAsync(runner);

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
        var runner = _WarmupRunner(provider);

        // when
        await _StartAsync(runner);
        var ready = ((IInitializer)runner).IsInitialized;
        await _StopAsync(runner);

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
        var runner = _WarmupRunner(provider);

        // when
        await _StartAsync(runner);
        var ready = ((IInitializer)runner).IsInitialized;
        await _StopAsync(runner);

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
            .GetServices<IStatefulResource>()
            .Single(r => string.Equals(r.Type, "MaxMind", StringComparison.Ordinal));

        // when
        await resource.SetupAsync(AbortToken);

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

    private static IHostedLifecycleService _WarmupRunner(IServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<IInitializer>().Cast<IHostedLifecycleService>().Single();

    private static async Task _StartAsync(IHostedLifecycleService runner)
    {
        await runner.StartingAsync(AbortToken);
        await runner.StartAsync(AbortToken);
        await runner.StartedAsync(AbortToken);
    }

    private static async Task _StopAsync(IHostedLifecycleService runner)
    {
        await runner.StoppingAsync(AbortToken);
        await runner.StopAsync(AbortToken);
        await runner.StoppedAsync(AbortToken);
    }
}
