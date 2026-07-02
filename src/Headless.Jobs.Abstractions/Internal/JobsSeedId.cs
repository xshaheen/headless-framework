// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;

namespace Headless.Jobs.Internal;

/// <summary>
/// Deterministic, stable identifiers for seeded jobs. A seeded cron row derives its primary key from the function
/// name, so concurrent first-boot across nodes converges on a single row (primary-key dedup) instead of each node
/// inserting a distinct <see cref="Guid.NewGuid"/> row and double-scheduling the function. The optional Jobs
/// distributed lock then only removes the redundant cross-node scan/write storm — it is no longer load-bearing for
/// duplicate suppression.
/// </summary>
internal static class JobsSeedId
{
    // Stable prefix so the cron-seed id space cannot collide with any future deterministic id space.
    private const string _CronSeedPrefix = "headless.jobs.cron-seed:";

    /// <summary>Deterministic primary key for the seeded cron row of <paramref name="function"/>.</summary>
    public static Guid ForCronSeed(string function)
    {
        var input = Encoding.UTF8.GetBytes(_CronSeedPrefix + function);
        var hash = SHA256.HashData(input);

        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);

        // Stamp a well-formed deterministic GUID: version 8 (RFC 9562 custom) + RFC 4122 variant. The value only has
        // to be stable and collision-free across nodes for the same function — it is generated and consumed by Jobs.
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x80);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        return new Guid(guidBytes);
    }
}
