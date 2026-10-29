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
    /// <see cref="ScheduleDiffers"/>) or its payload drifted (see <see cref="PayloadDiffers"/>).
    /// </summary>
    public static bool Differs(CronJobEntity stored, in CronSeedDefinition entry)
    {
        return ScheduleDiffers(stored, entry) || PayloadDiffers(stored, entry);
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
    /// Whether the seed must rewrite the stored payload and contract version of <paramref name="stored"/> to match
    /// <paramref name="entry"/>.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> for an entry without a request hash, whose payload the seed never rewrites. Otherwise
    /// <see langword="true"/> when the stored contract version differs, when the stored payload has no canonical form,
    /// or when its canonical hash differs from the entry's.
    /// </returns>
    public static bool PayloadDiffers(CronJobEntity stored, in CronSeedDefinition entry)
    {
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
