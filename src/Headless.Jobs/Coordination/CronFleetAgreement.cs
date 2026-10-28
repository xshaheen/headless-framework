// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
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
    ILogger<CronFleetAgreement> logger
)
{
    // An I/O bound, so it waits real time. A healthy snapshot read is one cheap query; a store that cannot answer in
    // this long is treated as unavailable and the pass retries soon, rather than holding a startup drain open.
    private static readonly TimeSpan _SnapshotTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Reads the fleet's published rules digests and compares them with the one this node published.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<CronFleetAgreementResult> CheckAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<NodeLivenessSnapshot> snapshot;

        using (var timeout = new CancellationTokenSource(_SnapshotTimeout))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
        {
            try
            {
                snapshot = await membership.GetLivenessSnapshotAsync(linked.Token).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Any failure to read the fleet means agreement cannot be shown; the pass defers and retries, and the cause is logged.
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                CronFleetAgreementLog.MembershipUnavailable(logger, exception);

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

        return disagreeing is null ? CronFleetAgreementResult.Agreed : CronFleetAgreementResult.Disagreed(disagreeing);
    }

    // A node without the key is not a Jobs node (Messaging shares the cluster), and one that publishes false never
    // sweeps, so neither can rewrite a row and neither may hold every rebase back.
    private static bool _RunsTheSweep(NodeLivenessSnapshot node)
    {
        return string.Equals(
            node.Metadata.GetValueOrDefault(CronRulesDigest.FingerprintSweepMetadataKey),
            "true",
            StringComparison.Ordinal
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

internal static partial class CronFleetAgreementLog
{
    [LoggerMessage(
        EventId = 3234,
        Level = LogLevel.Warning,
        Message = "Cron fingerprint rebases deferred: the coordination membership snapshot could not be read, so fleet "
            + "agreement on cron rules cannot be shown. The next sweep retries within five minutes."
    )]
    internal static partial void MembershipUnavailable(ILogger logger, Exception exception);
}
