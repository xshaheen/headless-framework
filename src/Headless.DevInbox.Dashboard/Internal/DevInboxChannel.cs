// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Headless.Dashboard;

namespace Headless.DevInbox.Dashboard.Internal;

/// <summary>The shared shape of a channel over a store of <typeparamref name="TCapture"/>.</summary>
internal abstract class DevInboxChannel<TCapture, TRow, TDetail> : IDevInboxChannel
    where TCapture : class
{
    public abstract string Kind { get; }

    public abstract string CountKey { get; }

    public string InstanceId { get; } = Guid.NewGuid().ToString("N");

    public abstract int Capacity { get; }

    public abstract int Count { get; }

    public abstract long Version { get; }

    public abstract JsonTypeInfo<TRow> RowType { get; }

    public abstract JsonTypeInfo<TDetail> DetailType { get; }

    public abstract bool Delete(Guid id);

    public abstract TCapture? Get(Guid id);

    /// <summary>The store's newest-first captures matching <paramref name="search"/>, after a sequence when given.</summary>
    public abstract IReadOnlyList<TCapture> Query(string? search, long? afterSequence);

    public abstract Guid IdOf(TCapture capture);

    public abstract long SequenceOf(TCapture capture);

    public abstract TRow ToRow(TCapture capture);

    public abstract TDetail ToDetail(TCapture capture);

    protected abstract string TitleOf(TCapture capture);

    protected abstract bool FailedOf(TCapture capture);

    protected abstract string? TraceIdOf(TCapture capture);

    /// <summary>
    /// One page of the newest-first list below the sequence <paramref name="before"/>. The stores page only forwards,
    /// so the page filters the newest-first snapshot, which the store's capacity keeps small.
    /// </summary>
    public (IReadOnlyList<TRow> Items, long? NextCursor) List(string? search, long? before, int limit)
    {
        var page = Query(search, afterSequence: null)
            .Where(capture => before is not { } cursor || SequenceOf(capture) < cursor)
            .Take(limit + 1)
            .ToList();

        long? next = page.Count > limit ? SequenceOf(page[limit - 1]) : null;

        return ([.. page.Take(limit).Select(ToRow)], next);
    }

    public IReadOnlyList<(Guid Id, long Sequence)> Snapshot(string? search)
    {
        return [.. Query(search, afterSequence: null).Select(c => (IdOf(c), SequenceOf(c)))];
    }

    public IReadOnlyList<(Guid Id, long Sequence, JsonElement Row)> RowsAfter(long sequence)
    {
        return
        [
            .. Query(search: null, afterSequence: sequence)
                .Select(c => (IdOf(c), SequenceOf(c), JsonSerializer.SerializeToElement(ToRow(c), RowType))),
        ];
    }

    public DashboardObject? Describe(string moduleId, Guid id)
    {
        if (Get(id) is not { } capture)
        {
            return null;
        }

        return new DashboardObject
        {
            Target = new ObjectRef
            {
                Module = moduleId,
                Kind = Kind,
                Id = id.ToString("D"),
            },
            Title = TitleOf(capture),
            Status = FailedOf(capture) ? "Failed" : null,
            TraceId = TraceIdOf(capture),
        };
    }
}
