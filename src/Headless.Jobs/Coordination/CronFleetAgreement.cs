// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Instrumentation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Jobs.Coordination;

/// <summary>
/// Decides whether every live node that runs the fingerprint sweep evaluates cron under the same rules as this node, so
/// a stale definition is rebased once per real rule change instead of being rewritten back and forth by nodes that
/// disagree.
/// </summary>
/// <remarks>
/// Registered only on the durable store, where nodes share one coordination cluster. The in-memory store is one
/// process, so it has no fleet to agree with and never consults this.
/// </remarks>
internal sealed class CronFleetAgreement(
    INodeMembership membership,
    IOptions<CoordinationOptions> coordinationOptions,
    IJobsOwnerIdentity ownerIdentity,
    TimeProvider timeProvider,
    ILogger<CronFleetAgreement> logger
)
{
    // An I/O bound, so it waits real time. A healthy snapshot read is one cheap query; a store that cannot answer in
    // this long is treated as unavailable and the pass retries soon, rather than holding a startup drain open.
    private static readonly TimeSpan _SnapshotTimeout = TimeSpan.FromSeconds(10);

    // A rolling deploy disagrees for minutes; an hour means a node that will not converge on its own.
    private static readonly TimeSpan _HoldoutWarningAfter = TimeSpan.FromHours(1);

    private const string _NoDigest = "<none>";

    // The drain and the periodic sweep can check at the same moment, and both update the one episode.
    private readonly Lock _episodeLock = new();
    private string? _episodeKey;
    private long _episodeStartedAt;
    private bool _episodeWarned;

    /// <summary>Reads the fleet's published rules digests and compares them with the one this node published.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<CronFleetAgreementResult> CheckAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<NodeLivenessSnapshot> snapshot;

        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            linked.CancelAfter(_SnapshotTimeout);

            try
            {
                snapshot = await membership.GetLivenessSnapshotAsync(linked.Token).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Any failure to read the fleet means agreement cannot be shown; the pass defers and retries, and the cause is logged.
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                // Not a disagreement: no node can be named, so this neither starts nor ends a holdout episode.
                JobsFingerprintSweepLog.MembershipUnavailable(logger, exception);
                JobsMetrics.FingerprintSweepDeferred(CronFingerprintSweepDeferralReason.MembershipUnavailable);

                return CronFleetAgreementResult.Unavailable;
            }
        }

        // Compared against what this node published, never a recomputation: peers judge this node by that value.
        var local = coordinationOptions.Value.Metadata.GetValueOrDefault(CronRulesDigest.DigestMetadataKey);
        var self = ownerIdentity.TryGetStampOwner(out var owner) ? owner : null;
        List<CronFleetPeer>? disagreeing = null;

        foreach (var node in snapshot)
        {
            if (
                node.State is not (NodeLivenessState.Alive or NodeLivenessState.Suspected)
                || !_RunsTheSweep(node)
                || string.Equals(node.Identity.ToString(), self, StringComparison.Ordinal)
            )
            {
                continue;
            }

            var digest = node.Metadata.GetValueOrDefault(CronRulesDigest.DigestMetadataKey);

            if (!string.Equals(digest, local, StringComparison.Ordinal))
            {
                (disagreeing ??= []).Add(new CronFleetPeer(node.Identity, digest));
            }
        }

        if (disagreeing is null)
        {
            _EndEpisode();

            return CronFleetAgreementResult.Agreed;
        }

        JobsMetrics.FingerprintSweepDeferred(CronFingerprintSweepDeferralReason.FleetDisagreement);
        _TrackEpisode(disagreeing, local);

        return CronFleetAgreementResult.Disagreed(disagreeing);
    }

    // A node without the key is not a Jobs node (Messaging shares the cluster), and one that publishes false never
    // sweeps, so neither can rewrite a row and neither may hold every rebase back.
    private static bool _RunsTheSweep(NodeLivenessSnapshot node)
    {
        return string.Equals(
            node.Metadata.GetValueOrDefault(CronRulesDigest.FingerprintSweepMetadataKey),
            CronRulesDigest.SweepEnabled,
            StringComparison.Ordinal
        );
    }

    private void _EndEpisode()
    {
        lock (_episodeLock)
        {
            _episodeKey = null;
            _episodeWarned = false;
        }
    }

    // An episode is one unchanged set of disagreeing (node incarnation, digest) pairs, timed on the monotonic clock. A
    // peer that restarts still disagreeing is a new incarnation, so it starts a new episode and earns its own warning.
    private void _TrackEpisode(List<CronFleetPeer> disagreeing, string? local)
    {
        var key = string.Join(
            '\n',
            disagreeing.Select(static peer => $"{peer.Identity}={peer.Digest}").Order(StringComparer.Ordinal)
        );

        lock (_episodeLock)
        {
            if (!string.Equals(key, _episodeKey, StringComparison.Ordinal))
            {
                _episodeKey = key;
                _episodeStartedAt = timeProvider.GetTimestamp();
                _episodeWarned = false;

                return;
            }

            if (_episodeWarned || timeProvider.GetElapsedTime(_episodeStartedAt) < _HoldoutWarningAfter)
            {
                return;
            }

            _episodeWarned = true;
        }

        JobsFingerprintSweepLog.FleetHoldout(
            logger,
            _HoldoutWarningAfter,
            local ?? _NoDigest,
            _DescribeGroups(disagreeing)
        );
    }

    // "digest: node@1, node@2; digest: node@3", so an operator sees which nodes to upgrade or restart.
    private static string _DescribeGroups(List<CronFleetPeer> disagreeing)
    {
        return string.Join(
            "; ",
            disagreeing
                .GroupBy(static peer => peer.Digest ?? _NoDigest, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(static group =>
                    $"{group.Key}: {string.Join(", ", group.Select(static peer => peer.Identity.ToString()).Order(StringComparer.Ordinal))}"
                )
        );
    }
}

/// <summary>A live sweeping peer and the digest it published, or <see langword="null"/> when it published none.</summary>
internal sealed record CronFleetPeer(NodeIdentity Identity, string? Digest);

/// <summary>Outcome of one fleet agreement check.</summary>
internal sealed record CronFleetAgreementResult
{
    public static CronFleetAgreementResult Agreed { get; } = new() { Outcome = CronFleetAgreementOutcome.Agreed };

    public static CronFleetAgreementResult Unavailable { get; } =
        new() { Outcome = CronFleetAgreementOutcome.Unavailable };

    public required CronFleetAgreementOutcome Outcome { get; init; }

    /// <summary>The sweeping peers whose digest differs from this node's; empty unless the fleet disagreed.</summary>
    public IReadOnlyList<CronFleetPeer> DisagreeingPeers { get; init; } = [];

    public static CronFleetAgreementResult Disagreed(IReadOnlyList<CronFleetPeer> peers) =>
        new() { Outcome = CronFleetAgreementOutcome.Disagreed, DisagreeingPeers = peers };
}

internal enum CronFleetAgreementOutcome
{
    Agreed = 1,
    Disagreed = 2,
    Unavailable = 3,
}
