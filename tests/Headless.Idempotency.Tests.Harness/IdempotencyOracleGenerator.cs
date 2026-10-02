// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Headless.Idempotency;

namespace Tests;

/// <summary>
/// Generates idempotency histories from a seed with <see cref="System.Random" /> alone, so any history is replayed
/// exactly by its seed.
/// </summary>
public static class IdempotencyOracleGenerator
{
    /// <summary>The marker for the key whose length is exactly <see cref="IdempotencyFieldLimits.KeyMaxLength" />.</summary>
    internal const string LongestKey = "\0longest";

    /// <summary>The marker for the key one character past <see cref="IdempotencyFieldLimits.KeyMaxLength" />.</summary>
    internal const string TooLongKey = "\0too-long";

    /// <summary>
    /// Tenant ids, <see langword="null" /> being the host scope. Case, precomposed-versus-decomposed accents, and a
    /// dotted capital I are distinct ordinal keys every provider must keep apart; a NUL is refused before any write.
    /// </summary>
    internal static readonly string?[] Tenants =
    [
        null,
        "tenant-a",
        "Tenant-A",
        "tenant-\u00e9",
        "tenant-e\u0301",
        "t\u0130",
        "t\u0000x",
    ];

    /// <summary>
    /// Key suffixes appended to a per-run prefix: case, accent composition, sharp s, an interior space, a zero-width
    /// space, a surrogate pair, a control character, NUL and a lone surrogate (both refused), and the longest allowed
    /// key and one past it.
    /// </summary>
    internal static readonly string[] Keys =
    [
        "job",
        "Job",
        "caf\u00e9",
        "cafe\u0301",
        "stra\u00dfe",
        "strasse",
        "a b",
        "r\u200b",
        "\ud83d\ude00",
        "x\u0001y",
        "x\u0000y",
        "lone\ud800",
        LongestKey,
        TooLongKey,
    ];

    /// <summary>The request fingerprints a history admits with.</summary>
    internal static readonly IdempotencyFingerprint[] Fingerprints =
    [
        IdempotencyFingerprint.Compute("request-a"),
        IdempotencyFingerprint.Compute("request-b"),
    ];

    /// <summary>The result contracts; index 0 of an admission's expectation means none.</summary>
    internal static readonly string[] Contracts = ["result/v1", "result/v2"];

    /// <summary>The recovery points a history records.</summary>
    internal static readonly string[] Points = ["step-1", "step-2"];

    // Ordinary keys every provider stores: the first tenants and keys of each pool.
    private const int _OrdinaryTenants = 3;
    private const int _OrdinaryKeys = 8;

    private static readonly TimeSpan[] _Leases =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
    ];

    private static readonly TimeSpan[] _Retentions =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(10),
    ];

    // Sub-microsecond remainders: PostgreSQL stores microseconds, SQL Server and .NET 100-nanosecond ticks.
    private static readonly long[] _RemainderTicks = [0, 0, 1, 7, 15];

    private static readonly TimeSpan[] _Shifts =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(90),
        TimeSpan.FromSeconds(150),
        TimeSpan.FromSeconds(210),
        TimeSpan.FromSeconds(330),
        TimeSpan.FromSeconds(630),
    ];

    // Offset by 15 seconds from every multiple of 30, so no purge cutoff lands on a retention boundary.
    private static readonly TimeSpan[] _PurgeAges =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(75),
        TimeSpan.FromSeconds(255),
        TimeSpan.FromSeconds(1215),
        TimeSpan.MaxValue,
    ];

    /// <summary>Generates one history of <paramref name="length" /> operations over 3 to 5 keys.</summary>
    /// <param name="seed">The seed; the same seed always yields the same history.</param>
    /// <param name="length">The number of operations.</param>
    /// <param name="edgeKeys">
    /// Whether keys draw from the whole edge-case pools, or only from ordinary values every provider can store (used to
    /// keep a run going past a key-shape divergence it already reported).
    /// </param>
    public static IdempotencyOracleHistory Generate(int seed, int length = 40, bool edgeKeys = true)
    {
        var random = new Random(seed);
        var keyCount = random.Next(3, 6);
        var keys = new List<IdempotencyOracleKey>(keyCount);
        var tenantPool = edgeKeys ? Tenants.Length : _OrdinaryTenants;
        var keyPool = edgeKeys ? Keys.Length : _OrdinaryKeys;

        while (keys.Count < keyCount)
        {
            var key = new IdempotencyOracleKey(random.Next(tenantPool), random.Next(keyPool));

            if (!keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        var ops = new List<IdempotencyOracleOp>(length);

        for (var i = 0; i < length; i++)
        {
            ops.Add(_NextOp(random, keyCount));
        }

        return new IdempotencyOracleHistory(seed, keys, ops);
    }

    /// <summary>Returns the key text a pool entry stands for under one run's prefix.</summary>
    internal static string KeyText(int index, string runId)
    {
        var prefix = runId + ":";

        return Keys[index] switch
        {
            LongestKey => prefix + new string('m', IdempotencyFieldLimits.KeyMaxLength - prefix.Length),
            TooLongKey => prefix + new string('m', IdempotencyFieldLimits.KeyMaxLength + 1 - prefix.Length),
            var suffix => prefix + suffix,
        };
    }

    private static IdempotencyOracleOp _NextOp(Random random, int keyCount)
    {
        var key = random.Next(keyCount);
        var roll = random.Next(100);

        return roll switch
        {
            < 20 => new IdempotencyOracleOp.Admit(
                key,
                _Fingerprint(random),
                random.Next(3),
                _Duration(random, _Leases),
                _Duration(random, _Retentions)
            ),
            < 25 => new IdempotencyOracleOp.Contend(
                key,
                2 << random.Next(3),
                _Duration(random, _Leases),
                _Duration(random, _Retentions)
            ),
            < 27 => new IdempotencyOracleOp.AdmitPadded(key, random.Next(2)),
            < 33 => new IdempotencyOracleOp.EnlistedAdmit(
                key,
                _Fingerprint(random),
                _Duration(random, _Leases),
                _Duration(random, _Retentions),
                random.Next(2) == 0,
                random.Next(3) != 0
            ),
            < 43 => new IdempotencyOracleOp.Complete(
                key,
                _Slot(random),
                random.Next(Contracts.Length),
                random.Next(3) == 0 ? _Duration(random, _Retentions) : null
            ),
            < 46 => new IdempotencyOracleOp.ContendComplete(key, _Slot(random), 2 << random.Next(3)),
            < 52 => new IdempotencyOracleOp.SetRecoveryPoint(key, _Slot(random), random.Next(Points.Length)),
            < 58 => new IdempotencyOracleOp.Release(key, _Slot(random)),
            < 65 => new IdempotencyOracleOp.Renew(key, _Slot(random), _Duration(random, _Leases)),
            < 70 => new IdempotencyOracleOp.FenceAndComplete(key, _Slot(random), random.Next(3) != 0),
            < 76 => new IdempotencyOracleOp.Peek(key),
            < 80 => new IdempotencyOracleOp.Purge(_PurgeAges[random.Next(_PurgeAges.Length)]),
            _ => new IdempotencyOracleOp.AdvanceTime(_Shifts[random.Next(_Shifts.Length)]),
        };
    }

    // Mostly the first fingerprint, so most repeated admissions of a key agree with the stored one.
    private static int _Fingerprint(Random random)
    {
        return random.Next(5) == 0 ? 1 : 0;
    }

    private static TimeSpan _Duration(Random random, TimeSpan[] pool)
    {
        return pool[random.Next(pool.Length)]
            + TimeSpan.FromTicks(_RemainderTicks[random.Next(_RemainderTicks.Length)]);
    }

    // Mostly the latest admission, sometimes an older (stale) one, rarely one never issued.
    private static int _Slot(Random random)
    {
        var roll = random.Next(10);

        return roll < 7 ? 0
            : roll < 9 ? 1
            : 9;
    }

    internal static string DescribeTenant(int index)
    {
        return Tenants[index] is { } tenant ? Escape(tenant) : "host";
    }

    internal static string DescribeKey(int index)
    {
        return Keys[index] switch
        {
            LongestKey => "longest",
            TooLongKey => "too-long",
            var suffix => Escape(suffix),
        };
    }

    internal static string Escape(string value)
    {
        var builder = new StringBuilder("\"");

        foreach (var c in value)
        {
            builder.Append(c is < ' ' or > '~' ? $"\\u{(int)c:x4}" : c.ToString());
        }

        return builder.Append('"').ToString();
    }
}
