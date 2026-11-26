// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Dashboard;

namespace Headless.DevInbox.Dashboard.Internal;

/// <summary>
/// The captures each configured channel took since the last read, as change events with each row's list projection.
/// The cursor is <c>e&lt;n&gt;.s&lt;n&gt;.p&lt;n&gt;</c>: the last sequence read from each channel's store. The stores live in
/// one process, so the cursor is per process too.
/// </summary>
internal sealed class DevInboxChangeSource(string moduleId, DevInboxChannels channels)
{
    private readonly string _moduleId = Argument.IsNotNullOrWhiteSpace(moduleId);
    private readonly DevInboxChannels _channels = Argument.IsNotNull(channels);

    public string StartCursor() =>
        _Format(_channels.All.ToDictionary(static c => c.Kind, static c => c.Version, StringComparer.Ordinal));

    public DashboardChangeBatch ReadChanges(string cursor)
    {
        // A cursor this process cannot read, such as one from before a restart, starts again from now.
        if (!_TryParse(cursor, out var positions))
        {
            return new DashboardChangeBatch { Changes = [], Cursor = StartCursor() };
        }

        var changes = new List<DashboardChange>();
        var next = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var channel in _channels.All)
        {
            var after = positions.GetValueOrDefault(channel.Kind, channel.Version);
            var rows = channel.RowsAfter(after);
            next[channel.Kind] = rows.Count == 0 ? after : Math.Max(after, rows.Max(static row => row.Sequence));

            // Oldest first, so the stream sends captures in the order they arrived.
            for (var i = rows.Count - 1; i >= 0; i--)
            {
                var (id, sequence, row) = rows[i];
                changes.Add(
                    new DashboardChange
                    {
                        Target = new ObjectRef
                        {
                            Module = _moduleId,
                            Kind = channel.Kind,
                            Id = id.ToString("D"),
                        },
                        Version = sequence.ToString(CultureInfo.InvariantCulture),
                        Projection = row,
                    }
                );
            }
        }

        return new DashboardChangeBatch { Changes = changes, Cursor = _Format(next) };
    }

    private string _Format(Dictionary<string, long> positions) =>
        string.Join(
            '.',
            _channels.All.Select(channel =>
                _Prefix(channel.Kind) + positions[channel.Kind].ToString(CultureInfo.InvariantCulture)
            )
        );

    private bool _TryParse(string cursor, out Dictionary<string, long> positions)
    {
        positions = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var part in cursor.Split('.'))
        {
            var channel = _channels.All.FirstOrDefault(c => part.StartsWith(_Prefix(c.Kind), StringComparison.Ordinal));

            if (
                channel is null
                || !long.TryParse(part.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var position)
            )
            {
                return false;
            }

            positions[channel.Kind] = position;
        }

        return positions.Count == _channels.All.Count;
    }

    private static string _Prefix(string kind) => kind[..1];
}
