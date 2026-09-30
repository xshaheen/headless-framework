// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Tests;

/// <summary>One lease key of a generated history, as indexes into the generator's edge-case pools.</summary>
public sealed record FencingOracleKey(int TenantIndex, int KindIndex, int ResourceIndex);

/// <summary>A generated history: the seed that produced it, the keys it touches, and its operations in order.</summary>
public sealed record FencingOracleHistory(
    int Seed,
    IReadOnlyList<FencingOracleKey> Keys,
    IReadOnlyList<FencingOracleOp> Ops
)
{
    /// <summary>The same history restricted to <paramref name="ops" />, for minimization.</summary>
    public FencingOracleHistory With(IReadOnlyList<FencingOracleOp> ops)
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
                                $"k{i}=({FencingOracleGenerator.DescribeTenant(k.TenantIndex)}, kind{k.KindIndex}, {FencingOracleGenerator.DescribeResource(k.ResourceIndex)})"
                            )
                    )
                ),
        };

        lines.AddRange(Ops.Select((op, i) => string.Create(CultureInfo.InvariantCulture, $"  {i:D3}: {op}")));

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>One step of a generated lease-operation history.</summary>
/// <remarks>
/// Durations are whole minutes, optionally plus a sub-microsecond remainder that probes timestamp precision; time
/// moves only through <see cref="AdvanceTime" />, in multiples of 30 seconds. An expiry decision is therefore either
/// at least 30 seconds from its boundary or exactly on it, where the milliseconds of real time a step takes make it
/// expired on every provider. That is what makes a history deterministic across engines with different clocks.
/// </remarks>
public abstract record FencingOracleOp
{
    /// <summary>Grants the lease on key <paramref name="Key" />.</summary>
    public sealed record Grant(int Key, TimeSpan Duration) : FencingOracleOp;

    /// <summary><paramref name="Contenders" /> concurrent autonomous grants of key <paramref name="Key" />.</summary>
    public sealed record Contend(int Key, int Contenders, TimeSpan Duration) : FencingOracleOp;

    /// <summary>A grant whose kind, resource, or tenant is padded with whitespace; refused before any write.</summary>
    public sealed record GrantPadded(int Key, int Part) : FencingOracleOp;

    /// <summary>
    /// Renews through a generation the history already saw for the key: <paramref name="Slot" /> 0 is the latest,
    /// higher slots are older (stale) ones; a slot beyond what was seen uses a generation no store ever issued.
    /// </summary>
    public sealed record Renew(int Key, int Slot, TimeSpan Duration, bool WithProgress) : FencingOracleOp;

    /// <summary>Settles through a generation the history saw for the key.</summary>
    public sealed record Settle(int Key, int Slot) : FencingOracleOp;

    /// <summary>Releases through a generation the history saw for the key.</summary>
    public sealed record Release(int Key, int Slot) : FencingOracleOp;

    /// <summary>Fences a generation inside a unit, settles it there when the fence passes, then commits or rolls back.</summary>
    public sealed record FenceAndSettle(int Key, int Slot, bool Commit) : FencingOracleOp;

    /// <summary>Grants inside a unit that commits or rolls back.</summary>
    public sealed record EnlistedGrant(int Key, TimeSpan Duration, bool Commit) : FencingOracleOp;

    /// <summary>Sweeps expired leases of one kind; the handler records each claimed lease and commits.</summary>
    public sealed record Sweep(int KindIndex, int Limit) : FencingOracleOp;

    /// <summary>Purges ended leases of one kind that ended at least <paramref name="OlderThan" /> ago.</summary>
    public sealed record Purge(int KindIndex, TimeSpan OlderThan) : FencingOracleOp;

    /// <summary>Moves every stored instant of the history's keys into the past: time passing, deterministically.</summary>
    public sealed record AdvanceTime(TimeSpan By) : FencingOracleOp;

    private FencingOracleOp() { }
}

/// <summary>What one step observed on one provider, normalized so two providers can be compared.</summary>
/// <param name="Summary">
/// Everything compared exactly: statuses, refusals, generations renumbered by first appearance (so gaps a store burns
/// never count), takeover counts, progress presence, sweep handoffs, purge counts, and the stored rows after the step.
/// </param>
/// <param name="TtlErrorTicks">
/// For a step that granted: how far the stored <c>expires_at - granted_at</c> is from the requested duration. Compared
/// within the coarser provider's timestamp precision, and reported raw.
/// </param>
public sealed record FencingOracleObservation(string Summary, long? TtlErrorTicks)
{
    public bool Matches(FencingOracleObservation other, long precisionTicks)
    {
        if (!string.Equals(Summary, other.Summary, StringComparison.Ordinal))
        {
            return false;
        }

        if (TtlErrorTicks is null || other.TtlErrorTicks is null)
        {
            return TtlErrorTicks is null && other.TtlErrorTicks is null;
        }

        return Math.Abs(TtlErrorTicks.Value - other.TtlErrorTicks.Value) < precisionTicks;
    }

    public override string ToString()
    {
        return TtlErrorTicks is { } error
            ? string.Create(CultureInfo.InvariantCulture, $"{Summary} ttlError={error}t")
            : Summary;
    }
}
