// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Internal;

/// <summary>
/// Decides whether a seeded row's arguments drifted from its seed entry, the one rule every store's seed reconcile
/// applies, so the stores cannot disagree about when a payload is rewritten.
/// </summary>
internal static class JobsSeedPayload
{
    /// <summary>
    /// Whether the seed must rewrite the stored payload and contract version of <paramref name="stored"/> to match
    /// <paramref name="entry"/>.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> for an entry without a request hash, whose payload the seed never rewrites. Otherwise
    /// <see langword="true"/> when the stored contract version differs, when the stored payload has no canonical form,
    /// or when its canonical hash differs from the entry's.
    /// </returns>
    public static bool Differs(CronJobEntity stored, in CronSeedDefinition entry)
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
