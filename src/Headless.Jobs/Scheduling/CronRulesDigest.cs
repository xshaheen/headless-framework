// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Headless.Jobs;

/// <summary>
/// One value naming every cron rule this node evaluates under: two nodes publish equal digests only when they compute
/// the same evaluation fingerprint for every IANA zone and for definitions that name no zone.
/// </summary>
/// <remarks>
/// It hashes the per-zone fingerprints rather than their raw inputs, so equal digests imply equal fingerprints by
/// construction, and any future change to what a fingerprint covers moves the digest with no separate version bump.
/// Fingerprints hash each operating system's own adjustment-rule representation, so a fleet mixing OS families, or
/// nodes with and without tzdata, never agrees.
/// </remarks>
internal static class CronRulesDigest
{
    /// <summary>Membership metadata key under which a durable Jobs node publishes its digest.</summary>
    internal const string DigestMetadataKey = "headless.jobs.cron-rules-digest";

    /// <summary>
    /// Membership metadata key carrying <c>true</c> when the node runs the fingerprint sweep and <c>false</c> when it
    /// does not. A node without the key is not a Jobs node.
    /// </summary>
    internal const string FingerprintSweepMetadataKey = "headless.jobs.fingerprint-sweep";

    // Bump when the canonical text below changes shape, so nodes on different formats never agree by accident.
    private const int _FormatVersion = 1;

    // .NET keeps a zone's rules for the process lifetime, so the digest cannot change while the process runs.
    private static readonly ConcurrentDictionary<TimeZoneInfo, string> _ByFallback = new(
        ReferenceEqualityComparer.Instance
    );

    /// <summary>The digest of this host's system zones under <paramref name="fallback"/>, computed once.</summary>
    public static string ForProcess(TimeZoneInfo fallback)
    {
        return _ByFallback.GetOrAdd(fallback, static zone => Compute(TimeZoneInfo.GetSystemTimeZones(), zone));
    }

    /// <summary>
    /// Computes the digest over <paramref name="systemZones"/>, each keyed by its IANA identifier, plus the
    /// <paramref name="fallback"/> zone that definitions with no zone are evaluated under.
    /// </summary>
    internal static string Compute(
        IEnumerable<TimeZoneInfo> systemZones,
        TimeZoneInfo fallback,
        int dstRuleVersion = CronEvaluationFingerprint.DstRuleVersion
    )
    {
        var fingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var zone in systemZones)
        {
            // Windows reports registry zones under Windows ids. Keyed by those, no zone would qualify and two Windows
            // nodes with different rules would publish the same digest, so each zone is keyed by its IANA id.
            string? ianaId;

            if (zone.HasIanaId)
            {
                ianaId = zone.Id;
            }
            else if (!TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out ianaId))
            {
                continue;
            }

            // The zone a definition naming this id is actually evaluated under, which is what its fingerprint covers.
            if (CronTimeZoneResolver.TryResolve(ianaId, fallback, out var resolved))
            {
                fingerprints[ianaId] = CronEvaluationFingerprint.Compute(resolved, dstRuleVersion);
            }
        }

        var builder = new StringBuilder(fingerprints.Count * 96);
        builder.Append("format=").Append(_FormatVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder
            .Append("fallback=")
            .Append(fallback.Id)
            .Append('|')
            .Append(CronEvaluationFingerprint.Compute(fallback, dstRuleVersion))
            .Append('\n');

        foreach (var (ianaId, fingerprint) in fingerprints)
        {
            builder.Append(ianaId).Append('|').Append(fingerprint).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
