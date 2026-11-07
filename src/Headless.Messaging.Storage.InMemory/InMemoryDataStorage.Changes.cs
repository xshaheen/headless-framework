// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Storage.InMemory;

// The change marker: every write that changes a row's visible state stamps it under the row's lock, and change reads
// range over it. Lease and lock writes leave it alone.
internal sealed partial class InMemoryDataStorage
{
    private readonly Lock _changeMarkerLock = new();
    private DateTimeOffset _lastChangeMarker = DateTimeOffset.MinValue;

    /// <summary>
    /// Stamps <paramref name="row"/>'s next change marker: the clock, or one tick past the later of the row's previous
    /// marker and the store's last marker when the clock has not moved past them, so two writes in one clock tick, or
    /// after the clock stepped back, still get increasing markers. The caller holds the row's lock or owns the row.
    /// </summary>
    private void _StampChange(MemoryMessage row)
    {
        lock (_changeMarkerLock)
        {
            var floor = row.ChangedAt > _lastChangeMarker ? row.ChangedAt : _lastChangeMarker;
            var now = timeProvider.GetUtcNow().ToUniversalTime();
            _lastChangeMarker = now > floor ? now : floor.AddTicks(1);
            row.ChangedAt = _lastChangeMarker;
        }
    }

    public ValueTask<DateTimeOffset?> GetLatestMessageChangeMarkerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset? latest = null;

        foreach (var row in PublishedMessages.Values.Concat(ReceivedMessages.Values))
        {
            if (row.ChangedAt != default && (latest is null || row.ChangedAt > latest))
            {
                latest = row.ChangedAt;
            }
        }

        return ValueTask.FromResult(latest);
    }

    public ValueTask<IReadOnlyList<MessageChange>> ReadChangedMessagesAsync(
        MessageChangeKind kind,
        DateTimeOffset after,
        DateTimeOffset through,
        MessageChangePosition? resumeAfter,
        int maxCount,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(maxCount);
        cancellationToken.ThrowIfCancellationRequested();

        var rows = kind switch
        {
            MessageChangeKind.Published => PublishedMessages.Values,
            MessageChangeKind.Received => ReceivedMessages.Values,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown change kind."),
        };

        IReadOnlyList<MessageChange> page =
        [
            .. rows.Select(static row => _Snapshot(row))
                .Where(row =>
                    row.ChangedAt > after
                    && row.ChangedAt <= through
                    && (
                        resumeAfter is not { } position
                        || row.ChangedAt > position.ChangedAt
                        || (row.ChangedAt == position.ChangedAt && row.Id.CompareTo(position.Id) > 0)
                    )
                )
                .OrderBy(static row => row.ChangedAt)
                .ThenBy(static row => row.Id)
                .Take(maxCount)
                .Select(row => new MessageChange
                {
                    Kind = kind,
                    Id = row.Id,
                    ChangedAt = row.ChangedAt,
                    Status = row.Status,
                }),
        ];

        return ValueTask.FromResult(page);
    }

    /// <summary>Reads the row's marker and status together, under its lock, so a read never pairs one write's marker with another's status.</summary>
    private static (Guid Id, DateTimeOffset ChangedAt, StatusName Status) _Snapshot(MemoryMessage row)
    {
        lock (row)
        {
            return (row.StorageId, row.ChangedAt, row.StatusName);
        }
    }
}
