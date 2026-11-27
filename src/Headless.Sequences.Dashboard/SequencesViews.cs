// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.

namespace Headless.Sequences;

/// <summary>The counts the stat strip and the mode chips show.</summary>
/// <param name="Total">The counters the scope and search match, at most 10,000.</param>
/// <param name="Capped">Whether more than 10,000 counters match, so the counts cover the first 10,000 in key order.</param>
/// <param name="Current">The matching counters in the partition their policy chooses now, at most 10,000.</param>
/// <param name="CurrentCapped">Whether <paramref name="Current"/> stopped at 10,000.</param>
/// <param name="NextResetAt">
/// The earliest next reset across every registered policy and the default policy, whatever the filters, or
/// <see langword="null"/> when no policy resets.
/// </param>
internal sealed record SequencesSummaryView(
    int Total,
    int Fast,
    int GapFree,
    int Reported,
    bool Capped,
    int Current,
    bool CurrentCapped,
    DateTimeOffset? NextResetAt
);

/// <summary>The policy a counter's name resolves to now.</summary>
/// <param name="TimeZone">The time zone id the date tokens and the reset period are decided in.</param>
internal sealed record SequencePolicyView(
    long Start,
    long Step,
    SequenceMode Mode,
    int BlockSize,
    SequenceReset Reset,
    string? Format,
    int FiscalYearStartMonth,
    string TimeZone
)
{
    public static SequencePolicyView From(SequencePolicy policy) =>
        new(
            policy.Start,
            policy.Step,
            policy.Mode,
            policy.BlockSize,
            policy.Reset,
            policy.Format,
            policy.FiscalYearStartMonth,
            policy.TimeZone.Id
        );
}

/// <summary>One stored counter.</summary>
/// <param name="Id">The counter's id, as <see cref="SequenceCounterId"/> formats it.</param>
/// <param name="TenantId">The owning tenant id, empty for the host scope.</param>
/// <param name="Partition">The partition, empty for none.</param>
/// <param name="PolicySource">
/// "registered" when the name has its own policy, "default" when it falls back to the default policy.
/// </param>
internal sealed record SequenceCounterView(
    string Id,
    string TenantId,
    bool IsHost,
    string Name,
    string Partition,
    long Value,
    DateTimeOffset UpdatedAt,
    string? LastNumber,
    bool IsCurrentPartition,
    DateTimeOffset? NextResetAt,
    SequenceMode Mode,
    SequenceReset Reset,
    string PolicySource,
    SequencePolicyView Policy
)
{
    public const string RegisteredPolicy = "registered";
    public const string DefaultPolicy = "default";

    public static SequenceCounterView From(SequenceCounter counter, SequencesOptions options) =>
        new(
            SequenceCounterId.Format(counter.Key),
            counter.Key.TenantId,
            counter.Key.TenantId.Length == 0,
            counter.Key.Name,
            counter.Key.Partition,
            counter.Value,
            counter.UpdatedAt,
            counter.LastNumber,
            counter.IsCurrentPartition,
            counter.NextResetAt,
            counter.Policy.Mode,
            counter.Policy.Reset,
            options.Policies.ContainsKey(counter.Key.Name) ? RegisteredPolicy : DefaultPolicy,
            SequencePolicyView.From(counter.Policy)
        );
}

/// <summary>One page of counters or of one counter's partitions.</summary>
/// <param name="NextCursor">The cursor of the next page, or <see langword="null"/> on the last page.</param>
internal sealed record SequenceCounterList(IReadOnlyList<SequenceCounterView> Items, string? NextCursor);

/// <summary>The serializer for every Sequences module response, generated so no response reflects at run time.</summary>
/// <remarks>Nulls are written, so the view tells a counter with no format or no reset from a missing field.</remarks>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never
)]
[JsonSerializable(typeof(SequencesSummaryView))]
[JsonSerializable(typeof(SequenceCounterView))]
[JsonSerializable(typeof(SequenceCounterList))]
internal sealed partial class SequencesDashboardJsonContext : JsonSerializerContext;
