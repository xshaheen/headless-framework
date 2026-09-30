// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Tests;

/// <summary>
/// Generates lease-operation histories from a seed with <see cref="System.Random" /> alone, so any history is replayed
/// exactly by its seed.
/// </summary>
public static class FencingOracleGenerator
{
    /// <summary>
    /// Tenant ids, <see langword="null" /> being the host scope. Case, precomposed-versus-decomposed accents, and a
    /// dotted capital I are distinct ordinal keys every provider must keep apart.
    /// </summary>
    internal static readonly string?[] Tenants =
    [
        null,
        "tenant-a",
        "Tenant-A",
        "tenant-\u00e9",
        "tenant-e\u0301",
        "t\u0130",
    ];

    /// <summary>Kind suffixes appended to a per-run prefix: two kinds that differ only by case.</summary>
    internal static readonly string[] KindSuffixes = ["-k", "-K"];

    /// <summary>
    /// Resources the call validation accepts: case, accent composition, sharp s, an interior space, a zero-width
    /// space, a surrogate pair, a control character, a lone surrogate, and the longest allowed value.
    /// </summary>
    internal static readonly string[] Resources =
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
        new('m', 256),
    ];

    private static readonly TimeSpan[] _Durations =
    [
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
    ];

    // Sub-microsecond remainders: PostgreSQL stores microseconds, SQL Server and .NET 100-nanosecond ticks.
    private static readonly long[] _DurationTicks = [0, 0, 1, 7, 15];

    private static readonly TimeSpan[] _Shifts =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(90),
        TimeSpan.FromSeconds(150),
        TimeSpan.FromSeconds(210),
        TimeSpan.FromSeconds(330),
        TimeSpan.FromSeconds(630),
    ];

    // Offset by 15 seconds from every multiple of 30, so no purge cutoff lands on an end time.
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
    /// Whether keys draw from the whole edge-case pools, or only from ordinary ASCII values that every provider can
    /// store (used to keep a run going past a key-shape divergence it already reported).
    /// </param>
    public static FencingOracleHistory Generate(int seed, int length = 40, bool edgeKeys = true)
    {
        var random = new Random(seed);
        var keyCount = random.Next(3, 6);
        var keys = new List<FencingOracleKey>(keyCount);
        var resourcePool = edgeKeys ? Resources.Length : 8;

        while (keys.Count < keyCount)
        {
            var key = new FencingOracleKey(
                random.Next(Tenants.Length),
                random.Next(KindSuffixes.Length),
                random.Next(resourcePool)
            );

            if (!keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        var ops = new List<FencingOracleOp>(length);

        for (var i = 0; i < length; i++)
        {
            ops.Add(_NextOp(random, keyCount));
        }

        return new FencingOracleHistory(seed, keys, ops);
    }

    private static FencingOracleOp _NextOp(Random random, int keyCount)
    {
        var key = random.Next(keyCount);
        var roll = random.Next(100);

        return roll switch
        {
            < 22 => new FencingOracleOp.Grant(key, _Duration(random)),
            < 27 => new FencingOracleOp.Contend(key, 2 << random.Next(3), _Duration(random)),
            < 29 => new FencingOracleOp.GrantPadded(key, random.Next(3)),
            < 41 => new FencingOracleOp.Renew(key, _Slot(random), _Duration(random), random.Next(2) == 0),
            < 48 => new FencingOracleOp.Settle(key, _Slot(random)),
            < 53 => new FencingOracleOp.Release(key, _Slot(random)),
            < 61 => new FencingOracleOp.FenceAndSettle(key, _Slot(random), random.Next(3) != 0),
            < 66 => new FencingOracleOp.EnlistedGrant(key, _Duration(random), random.Next(3) != 0),
            < 73 => new FencingOracleOp.Sweep(random.Next(KindSuffixes.Length), 1 + random.Next(4)),
            < 78 => new FencingOracleOp.Purge(
                random.Next(KindSuffixes.Length),
                _PurgeAges[random.Next(_PurgeAges.Length)]
            ),
            _ => new FencingOracleOp.AdvanceTime(_Shifts[random.Next(_Shifts.Length)]),
        };
    }

    private static TimeSpan _Duration(Random random)
    {
        return _Durations[random.Next(_Durations.Length)]
            + TimeSpan.FromTicks(_DurationTicks[random.Next(_DurationTicks.Length)]);
    }

    // Mostly the latest generation, sometimes an older (stale) one, rarely one never issued.
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

    internal static string DescribeResource(int index)
    {
        var resource = Resources[index];

        return resource.Length > 16
            ? string.Create(CultureInfo.InvariantCulture, $"'{resource[0]}'x{resource.Length}")
            : Escape(resource);
    }

    internal static string Escape(string value)
    {
        var builder = new System.Text.StringBuilder("\"");

        foreach (var c in value)
        {
            builder.Append(
                c is < ' ' or > '~' ? string.Create(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}") : c.ToString()
            );
        }

        return builder.Append('"').ToString();
    }
}
