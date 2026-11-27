// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Fencing;

namespace Headless.Leases;

/// <summary>The serializer for every Leases module response, generated so no response reflects at run time.</summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(LeasesSummaryView))]
[JsonSerializable(typeof(FencedLeaseList))]
[JsonSerializable(typeof(FencedLeaseCountsView))]
[JsonSerializable(typeof(FencedLeaseKindList))]
[JsonSerializable(typeof(FencedLeaseDetail))]
[JsonSerializable(typeof(DistributedLockList))]
[JsonSerializable(typeof(DistributedLockDetail))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class LeasesDashboardJsonContext : JsonSerializerContext;

/// <summary>Which halves the serving host has.</summary>
/// <param name="Fencing">The fenced-lease half, or <see langword="null"/> without Fencing.</param>
/// <param name="Locks">The distributed-lock half, or <see langword="null"/> without a real lock provider.</param>
internal sealed record LeasesSummaryView(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] LeasesFencingView? Fencing,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] LeasesLocksView? Locks
);

/// <summary>The fenced-lease half of the summary.</summary>
/// <param name="Registered">Always <see langword="true"/>: without Fencing the summary carries no object.</param>
internal sealed record LeasesFencingView(bool Registered);

/// <summary>The distributed-lock half of the summary.</summary>
/// <param name="Registered">Always <see langword="true"/>: without a lock provider the summary carries no object.</param>
internal sealed record LeasesLocksView(bool Registered);

/// <summary>Which of the three state groups a lease is in, as the store's status says.</summary>
internal enum FencedLeaseState
{
    Live,
    Expired,
    Ended,
}

/// <summary>One fenced lease, without its progress payload: the dashboard shows only its size and contract.</summary>
internal sealed record FencedLeaseView(
    string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? TenantId,
    string Kind,
    string Resource,
    long Generation,
    LeaseFenceStatus Status,
    FencedLeaseState State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Holder,
    int TakeoverCount,
    DateTimeOffset GrantedAt,
    DateTimeOffset ExpiresAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? EndedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ProgressContract,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? ProgressSizeBytes
)
{
    public static FencedLeaseView From(LeaseSnapshot snapshot)
    {
        var lease = snapshot.Lease;

        return new FencedLeaseView(
            FencedLeaseId.Format(lease.TenantId, lease.Kind, lease.Resource),
            lease.TenantId,
            lease.Kind,
            lease.Resource,
            lease.Generation,
            snapshot.Status,
            _StateOf(snapshot),
            snapshot.Holder,
            snapshot.TakeoverCount,
            snapshot.GrantedAt,
            snapshot.ExpiresAt,
            snapshot.EndedAt,
            snapshot.ProgressContract,
            snapshot.ProgressSizeBytes
        );
    }

    // The store's status decides the group; no clock here may move a lease between groups.
    private static FencedLeaseState _StateOf(LeaseSnapshot snapshot)
    {
        if (snapshot.IsLive)
        {
            return FencedLeaseState.Live;
        }

        return snapshot.IsExpired ? FencedLeaseState.Expired : FencedLeaseState.Ended;
    }
}

internal sealed record FencedLeaseList(
    IReadOnlyList<FencedLeaseView> Items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? NextCursor,
    DateTimeOffset ReadAt
);

internal sealed record LeaseCountView(int Count, bool Capped)
{
    public static LeaseCountView From(LeaseCount count) => new(count.Count, count.Capped);
}

internal sealed record FencedLeaseCountsView(
    LeaseCountView Live,
    LeaseCountView Expired,
    LeaseCountView Ended,
    LeaseCountView Total
)
{
    public static FencedLeaseCountsView From(FencedLeaseCounts counts) =>
        new(
            LeaseCountView.From(counts.Live),
            LeaseCountView.From(counts.Expired),
            LeaseCountView.From(counts.Ended),
            LeaseCountView.From(counts.Total)
        );
}

internal sealed record FencedLeaseKindList(IReadOnlyList<string> Items);

internal sealed record FencedLeaseDetail(FencedLeaseView Lease, DateTimeOffset ReadAt);

/// <summary>
/// One held lock as the provider lists it. No fencing token: providers report one only on the holder's own handle.
/// </summary>
/// <param name="TtlMilliseconds">The time to live at read time, or <see langword="null"/> for a lock with no expiry.</param>
internal sealed record DistributedLockView(
    string Id,
    string Resource,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? LeaseId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? TtlMilliseconds
)
{
    public static DistributedLockView From(DistributedLockInfo info) =>
        new(
            info.Resource,
            info.Resource,
            info.LeaseId,
            info.TimeToLive is { } ttl ? (long)Math.Floor(ttl.TotalMilliseconds) : null
        );
}

internal sealed record DistributedLockList(
    IReadOnlyList<DistributedLockView> Items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? NextCursor,
    long Total,
    DateTimeOffset ReadAt
);

internal sealed record DistributedLockDetail(DistributedLockView Lock, DateTimeOffset ReadAt);
