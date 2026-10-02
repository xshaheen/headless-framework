// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Tests;

/// <summary>One record key of a generated history, as indexes into the generator's edge-case pools.</summary>
public sealed record IdempotencyOracleKey(int TenantIndex, int KeyIndex);

/// <summary>A generated history: the seed that produced it, the keys it touches, and its operations in order.</summary>
public sealed record IdempotencyOracleHistory(
    int Seed,
    IReadOnlyList<IdempotencyOracleKey> Keys,
    IReadOnlyList<IdempotencyOracleOp> Ops
)
{
    /// <summary>The same history restricted to <paramref name="ops" />, for minimization.</summary>
    public IdempotencyOracleHistory With(IReadOnlyList<IdempotencyOracleOp> ops)
    {
        return this with { Ops = ops };
    }

    public string Describe()
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"seed={Seed}"),
            "keys: "
                + string.Join(
                    ", ",
                    Keys.Select(
                        (k, i) =>
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"k{i}=({IdempotencyOracleGenerator.DescribeTenant(k.TenantIndex)}, {IdempotencyOracleGenerator.DescribeKey(k.KeyIndex)})"
                            )
                    )
                ),
        };

        lines.AddRange(Ops.Select((op, i) => string.Create(CultureInfo.InvariantCulture, $"  {i:D3}: {op}")));

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>One step of a generated idempotency history.</summary>
/// <remarks>
/// Lease durations and retentions are whole minutes, optionally plus a sub-microsecond remainder that probes timestamp
/// precision; time moves only through <see cref="AdvanceTime" />, in multiples of 30 seconds. A lease or retention
/// decision is therefore either at least 30 seconds from its boundary or exactly on it, where the milliseconds of real
/// time a step takes make it elapsed on every provider. That is what makes a history deterministic across engines with
/// different clocks. An admission is named by a slot: 0 is the key's latest admitted attempt, higher slots are older
/// (stale) ones, and a slot beyond what the history saw names a generation no store ever issued.
/// </remarks>
public abstract record IdempotencyOracleOp
{
    /// <summary>Admits key <paramref name="Key" /> autonomously.</summary>
    /// <param name="Fingerprint">Which request fingerprint the call carries.</param>
    /// <param name="Expect">0 for no expected contract, otherwise the contract a replay must carry.</param>
    public sealed record Admit(int Key, int Fingerprint, int Expect, TimeSpan Lease, TimeSpan Retention)
        : IdempotencyOracleOp;

    /// <summary><paramref name="Racers" /> concurrent autonomous admissions of key <paramref name="Key" />.</summary>
    public sealed record Contend(int Key, int Racers, TimeSpan Lease, TimeSpan Retention) : IdempotencyOracleOp;

    /// <summary>An admission whose key or tenant is padded with whitespace; refused before any write.</summary>
    public sealed record AdmitPadded(int Key, int Part) : IdempotencyOracleOp;

    /// <summary>Admits inside a unit, completes there when admitted and asked to, then commits or rolls back.</summary>
    public sealed record EnlistedAdmit(
        int Key,
        int Fingerprint,
        TimeSpan Lease,
        TimeSpan Retention,
        bool CompleteInUnit,
        bool Commit
    ) : IdempotencyOracleOp;

    /// <summary>Completes an admission autonomously, under contract <paramref name="Contract" />.</summary>
    public sealed record Complete(int Key, int Slot, int Contract, TimeSpan? Retention) : IdempotencyOracleOp;

    /// <summary><paramref name="Racers" /> concurrent autonomous completions of one admission.</summary>
    public sealed record ContendComplete(int Key, int Slot, int Racers) : IdempotencyOracleOp;

    /// <summary>Records recovery point <paramref name="Point" /> for an admission, autonomously.</summary>
    public sealed record SetRecoveryPoint(int Key, int Slot, int Point) : IdempotencyOracleOp;

    /// <summary>Releases an admission autonomously.</summary>
    public sealed record Release(int Key, int Slot) : IdempotencyOracleOp;

    /// <summary>Renews an admission's lease.</summary>
    public sealed record Renew(int Key, int Slot, TimeSpan Lease) : IdempotencyOracleOp;

    /// <summary>Fences an admission inside a unit, completes it there when the fence passes, then commits or rolls back.</summary>
    public sealed record FenceAndComplete(int Key, int Slot, bool Commit) : IdempotencyOracleOp;

    /// <summary>Peeks at key <paramref name="Key" />'s status.</summary>
    public sealed record Peek(int Key) : IdempotencyOracleOp;

    /// <summary>Purges records at least <paramref name="OlderThan" /> past their retention.</summary>
    public sealed record Purge(TimeSpan OlderThan) : IdempotencyOracleOp;

    /// <summary>Moves every stored instant of the history's keys into the past: time passing, deterministically.</summary>
    public sealed record AdvanceTime(TimeSpan By) : IdempotencyOracleOp;

    private IdempotencyOracleOp() { }
}

/// <summary>What one step observed on one provider, normalized so two providers can be compared.</summary>
/// <param name="Summary">
/// Everything compared exactly: dispositions, refusals, generations renumbered by first appearance (so gaps a store
/// burns never count), replayed results, recovery points, peeks, purged keys, and the stored rows after the step.
/// </param>
/// <param name="PrecisionErrorTicks">
/// For a step that admitted and extended the retention in the same statement: how far the stored
/// <c>retention_until - lease_expires_at</c> is from the requested retention minus the requested lease. Compared within
/// the tolerance, and reported raw.
/// </param>
public sealed record IdempotencyOracleObservation(string Summary, long? PrecisionErrorTicks)
{
    public bool Matches(IdempotencyOracleObservation other, long precisionTicks)
    {
        if (!string.Equals(Summary, other.Summary, StringComparison.Ordinal))
        {
            return false;
        }

        if (PrecisionErrorTicks is null || other.PrecisionErrorTicks is null)
        {
            return PrecisionErrorTicks is null && other.PrecisionErrorTicks is null;
        }

        return Math.Abs(PrecisionErrorTicks.Value - other.PrecisionErrorTicks.Value) < precisionTicks;
    }

    public override string ToString()
    {
        return PrecisionErrorTicks is { } error
            ? string.Create(CultureInfo.InvariantCulture, $"{Summary} precisionError={error}t")
            : Summary;
    }
}
