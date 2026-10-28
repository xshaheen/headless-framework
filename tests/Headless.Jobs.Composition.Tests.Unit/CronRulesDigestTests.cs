// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// The node-wide cron rules digest: one value that is equal on two nodes only when they compute the same evaluation
/// fingerprint for every IANA zone and for definitions with no zone, and which every durable Jobs node publishes in
/// its membership metadata.
/// </summary>
public sealed class CronRulesDigestTests : TestBase
{
    private static readonly TimeZoneInfo _LosAngeles = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    private static readonly TimeZoneInfo _Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public void should_produce_the_same_digest_for_the_same_zones_and_fallback()
    {
        var first = CronRulesDigest.Compute([_LosAngeles, _Berlin], TimeZoneInfo.Utc);
        var second = CronRulesDigest.Compute([_Berlin, _LosAngeles], TimeZoneInfo.Utc);

        second.Should().Be(first, "zone enumeration order is not a rule difference");
        first.Should().HaveLength(64);
    }

    [Fact]
    public void should_produce_a_different_digest_for_a_different_fallback_zone()
    {
        var utc = CronRulesDigest.Compute([_LosAngeles, _Berlin], TimeZoneInfo.Utc);
        var berlin = CronRulesDigest.Compute([_LosAngeles, _Berlin], _Berlin);

        berlin.Should().NotBe(utc, "definitions without a zone fire under the fallback, so it is part of the rules");
    }

    [Fact]
    public void should_produce_a_different_digest_for_a_different_dst_rule_version()
    {
        var current = CronRulesDigest.Compute(
            [_LosAngeles, _Berlin],
            TimeZoneInfo.Utc,
            CronEvaluationFingerprint.DstRuleVersion
        );
        var next = CronRulesDigest.Compute(
            [_LosAngeles, _Berlin],
            TimeZoneInfo.Utc,
            CronEvaluationFingerprint.DstRuleVersion + 1
        );

        next.Should().NotBe(current);
    }

    [Fact]
    public void should_key_a_zone_reported_under_a_windows_id_by_its_iana_id()
    {
        // GetSystemTimeZones reports Windows ids on Windows. Keyed by that id alone, every zone would be skipped and
        // two Windows nodes with different rules would publish the same empty digest.
        var windowsNamed = TimeZoneInfo.CreateCustomTimeZone(
            "Pacific Standard Time",
            TimeSpan.FromHours(-8),
            "Pacific Standard Time",
            "Pacific Standard Time"
        );

        var viaWindowsId = CronRulesDigest.Compute([windowsNamed], TimeZoneInfo.Utc);

        viaWindowsId.Should().Be(CronRulesDigest.Compute([_LosAngeles], TimeZoneInfo.Utc));
        viaWindowsId.Should().NotBe(CronRulesDigest.Compute([], TimeZoneInfo.Utc));
    }

    [Fact]
    public void should_ignore_a_zone_with_no_iana_mapping()
    {
        var unmapped = TimeZoneInfo.CreateCustomTimeZone("Not A Real Zone", TimeSpan.FromHours(3), "x", "x");

        CronRulesDigest
            .Compute([_Berlin, unmapped], TimeZoneInfo.Utc)
            .Should()
            .Be(CronRulesDigest.Compute([_Berlin], TimeZoneInfo.Utc));
    }

    [Fact]
    public void should_publish_the_digest_and_a_true_sweep_flag_when_background_services_run()
    {
        var metadata = _PublishedMetadata(backgroundServices: true);

        metadata
            .Should()
            .Contain(
                CronRulesDigest.DigestMetadataKey,
                CronRulesDigest.Compute(TimeZoneInfo.GetSystemTimeZones(), TimeZoneInfo.Utc)
            );
        metadata.Should().Contain(CronRulesDigest.FingerprintSweepMetadataKey, "true");
    }

    [Fact]
    public void should_publish_a_false_sweep_flag_when_background_services_are_off()
    {
        var metadata = _PublishedMetadata(backgroundServices: false);

        metadata.Should().ContainKey(CronRulesDigest.DigestMetadataKey);
        metadata.Should().Contain(CronRulesDigest.FingerprintSweepMetadataKey, "false");
    }

    private static Dictionary<string, string> _PublishedMetadata(bool backgroundServices)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessCoordination(setup =>
            setup.UsePostgreSql("Host=localhost;Database=jobs;Username=jobs;Password=jobs")
        );
        services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(jobs =>
            (backgroundServices ? jobs : jobs.DisableBackgroundServices()).UseEntityFramework()
        );

        using var provider = services.BuildServiceProvider();

        // The same instance MembershipService copies into the node descriptor at registration.
        return provider.GetRequiredService<CoordinationOptions>().Metadata;
    }
}
