// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Two real nodes registered in one coordination cluster over a real store: a stale cron definition is rebased only
/// when every sweeping node published the same cron rules digest.
/// </summary>
/// <remarks>
/// The published keys are spelled out rather than taken from the framework's constants, because they are the contract
/// peers read across processes and releases.
/// </remarks>
public abstract class JobsCronFleetAgreementConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _DigestKey = "headless.jobs.cron-rules-digest";
    private const string _SweepKey = "headless.jobs.fingerprint-sweep";

    public virtual async Task a_stale_definition_stays_unrebased_while_a_sweeping_peer_publishes_other_rules()
    {
        var (result, fingerprint) = await _SweepWithPeerAsync(peerDigest: "digest-of-other-rules");

        result.DeferralReason.Should().Be(CronFingerprintSweepDeferralReason.FleetDisagreement);
        result.Rebased.Should().Be(0);
        fingerprint.Should().Be("stale");
    }

    public virtual async Task a_stale_definition_is_rebased_once_when_every_sweeping_node_publishes_the_same_rules()
    {
        var (result, fingerprint) = await _SweepWithPeerAsync(peerDigest: null);

        result.DeferralReason.Should().Be(CronFingerprintSweepDeferralReason.None);
        result.Rebased.Should().Be(1);
        fingerprint.Should().NotBe("stale");
    }

    // Both hosts publish the sweep flag, as nodes running background services would; the test drives node A's sweep
    // directly so no scheduler races it. A null peer digest leaves node B on the digest its own rules produce, which
    // matches node A's because both run in this process.
    private async Task<(CronFingerprintSweepResult Result, string? Fingerprint)> _SweepWithPeerAsync(string? peerDigest)
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var nodeA = fixture.BuildHost("fleet-a", configureServices: services => _Publish(services, null));
        using var nodeB = fixture.BuildHost("fleet-b", configureServices: services => _Publish(services, peerDigest));
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(nodeA, ct);
        await nodeA.StartAsync(ct);
        await nodeB.StartAsync(ct);

        try
        {
            var persistence = nodeA.Services.GetRequiredService<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
            >();
            var id = Guid.NewGuid();
            await persistence.InsertCronJobsAsync([_StaleDefinition(id)], ct);

            var result = await nodeA
                .Services.GetRequiredService<IInternalJobManager>()
                .RebaseStaleFingerprintsAsync(limit: 50, cancellationToken: ct);

            return (result, (await persistence.GetCronJobByIdAsync(id, ct))!.EvaluationFingerprint);
        }
        finally
        {
            await nodeB.StopAsync(ct);
            await nodeA.StopAsync(ct);
        }
    }

    private static void _Publish(IServiceCollection services, string? digest)
    {
        services.Configure<CoordinationOptions>(options =>
        {
            options.Metadata[_SweepKey] = "true";

            if (digest is not null)
            {
                options.Metadata[_DigestKey] = digest;
            }
        });
    }

    private static CronJobEntity _StaleDefinition(Guid id) =>
        new()
        {
            Id = id,
            Function = $"fleet-{id:N}",
            Expression = "0 * * * * *",
            ReconciledThroughUtc = DateTime.UtcNow.AddMinutes(-5),
            NextDueUtc = DateTime.UtcNow.AddDays(20),
            EvaluationFingerprint = "stale",
            OnMissedRun = MissedRunPolicy.Coalesce,
            MissedRunGraceSeconds = 60,
            Request = [],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
}
