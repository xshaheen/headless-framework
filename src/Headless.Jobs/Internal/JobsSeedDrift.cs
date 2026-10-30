// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Internal;

/// <summary>
/// Decides whether a seeded row drifted from its seed entry, the one rule every store's seed reconcile applies, so the
/// stores cannot disagree about when a row is restored or its payload rewritten.
/// </summary>
internal static class JobsSeedDrift
{
    /// <summary>
    /// Whether the seed must restore <paramref name="stored"/> to <paramref name="entry"/>: its schedule differs (see
    /// <see cref="ScheduleDiffers"/>), its payload drifted (see <see cref="PayloadDiffers"/>), or it was retired.
    /// </summary>
    public static bool Differs(CronJobEntity stored, in CronSeedDefinition entry)
    {
        return ScheduleDiffers(stored, entry)
            || PayloadDiffers(stored, entry)
            || JobsSeedId.IsRetiredMarkerOf(stored.InitIdentifier, entry.Marker);
    }

    /// <summary>
    /// Whether the stored expression or time zone of <paramref name="stored"/> differs from <paramref name="entry"/>'s.
    /// </summary>
    public static bool ScheduleDiffers(CronJobEntity stored, in CronSeedDefinition entry)
    {
        return !string.Equals(stored.Expression, entry.Expression, StringComparison.Ordinal)
            || !string.Equals(stored.TimeZoneId, entry.TimeZoneId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the seed must rewrite the stored job, payload, and contract version of <paramref name="stored"/> to
    /// match <paramref name="entry"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the stored row names another job, whatever the entry: its payload and version then
    /// belong to that job's contract, so the seed restores the entry's job with the entry's own payload and version.
    /// Otherwise <see langword="false"/> for an entry without a request hash, whose payload the seed never rewrites,
    /// and <see langword="true"/> when the stored contract version differs, when the stored payload has no canonical
    /// form, or when its canonical hash differs from the entry's.
    /// </returns>
    public static bool PayloadDiffers(CronJobEntity stored, in CronSeedDefinition entry)
    {
        if (!string.Equals(stored.Function, entry.Function, StringComparison.Ordinal))
        {
            return true;
        }

        if (entry.RequestHash is null)
        {
            return false;
        }

        return !string.Equals(stored.ContractVersion, entry.ContractVersion, StringComparison.Ordinal)
            || !string.Equals(
                JobsHelper.TryComputeCanonicalRequestHash(stored.Request ?? []),
                entry.RequestHash,
                StringComparison.Ordinal
            );
    }
}
